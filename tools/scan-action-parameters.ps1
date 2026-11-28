<#
.SYNOPSIS
    Derives, from the installed game assemblies, every terminal action that declares arguments,
    together with the type and the caption of EACH argument.

.DESCRIPTION
    The game does not publish argument names. It does something better: for every argument it
    builds a dialog whose caption is the argument's name, and the chain of methods that builds
    those dialogs is reachable from the action's registration. This script follows that chain.

    Three facts, each read from the game's own IL:

      * ARITY    - the number of `get_ParameterDefinitions` / `Add` pairs in the action's
                   registration. This is the game's own count, and it is authoritative.
      * TYPE     - the `unbox.any` targets of the action's `ActionWithParameters` handler, in
                   order, truncated to the arity. That is literally what the handler casts each
                   argument to before using it.
      * CAPTION  - the `MyStringId` caption of each `MyGuiScreenDialog*` constructed while
                   building the request, in construction order, truncated to the arity. The
                   dialogs are built by a chain of closure methods (`RequestRotationParameters`
                   builds the first, whose OnConfirmed builds the second, and so on), so the walk
                   is transitive: `ldftn`/`call` targets inside the game assemblies are followed
                   at the point they are named.

    The caption key is then resolved through the game's own `MyTexts` tables, so the name on
    screen is the game's own word for it, in both languages the mod ships.

    Nothing is executed: assemblies are loaded read-only and only metadata and IL are read.

.PARAMETER Bin
    Folder containing the game assemblies.

.PARAMETER Texts
    Folder containing MyTexts.resx and its per-language siblings.

.PARAMETER Out
    Writes the report here. Without it the report goes to stdout.

.PARAMETER EmitTable
    Emits the C# table (`ActionParameterTable.g.cs`) instead of the report.

.EXAMPLE
    pwsh -File tools/scan-action-parameters.ps1

.EXAMPLE
    pwsh -File tools/scan-action-parameters.ps1 -EmitTable -Out src/GridWorkspace.Mod/Data/Scripts/VGS/Model/ActionParameterTable.g.cs
#>
[CmdletBinding()]
param(
    [string] $Bin = 'H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64',
    [string] $Texts = 'H:\SteamLibrary\steamapps\common\SpaceEngineers\Content\Data\Localization',
    [string] $Out,
    [switch] $EmitTable
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

$gameAssemblies = @('Sandbox.Game.dll', 'SpaceEngineers.Game.dll', 'Sandbox.Common.dll')

# ---------------------------------------------------------------------------- text tables

function Read-Texts([string] $path) {
    $map = @{}
    if (-not (Test-Path $path)) { return $map }
    $xml = [xml](Get-Content $path -Raw)
    foreach ($d in $xml.root.data) {
        if ($d.name) { $map[$d.name] = $d.value }
    }
    return $map
}

$english = Read-Texts (Join-Path $Texts 'MyTexts.resx')
$russian = Read-Texts (Join-Path $Texts 'MyTexts.ru.resx')
if ($english.Count -eq 0) { throw "No English text table at $Texts" }

# ---------------------------------------------------------------------------- IL reading

# Operand size per opcode, taken from the runtime's own table so the walk cannot lose its place.
# Two-byte opcodes are keyed as 0xFE00 + value, which is also how the walk looks them up.
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
    $v = [int]$op.Value
    if ($v -lt 0) { $v += 0x10000 }
    $opSize[$v] = $size
}

# How many bytes this instruction occupies, prefix included.
function Get-InstructionLength([byte[]] $il, [int] $at) {
    $key = [int]$il[$at]
    $prefix = 0
    if ($key -eq 0xFE) {
        $key = 0xFE00 + [int]$il[$at + 1]
        $prefix = 1
    }
    if (-not $opSize.ContainsKey($key)) { return 1 + $prefix }
    $size = $opSize[$key]
    if ($size -eq -2) {
        $n = [BitConverter]::ToInt32($il, $at + 1 + $prefix)
        return 1 + $prefix + 4 + 4 * $n
    }
    return 1 + $prefix + $size
}

function Get-Il([System.Reflection.MethodBase] $method) {
    $body = $null
    try { $body = $method.GetMethodBody() } catch { return $null }
    if (-not $body) { return $null }
    return $body.GetILAsByteArray()
}

function Test-GameMethod($m) {
    if (-not $m) { return $false }
    $declaring = $m.DeclaringType
    if (-not $declaring) { return $false }
    $name = $declaring.Assembly.GetName().Name
    return ($name -eq 'Sandbox.Game' -or $name -eq 'SpaceEngineers.Game' -or $name -eq 'Sandbox.Common')
}

<#
    Reads one control-registration method and reports:

      Actions  - action id -> the methods its registration names (ldftn targets)
      Arity    - action id -> the number of `get_ParameterDefinitions` / `Add` pairs
      Unboxes  - method token -> the TypeCode names it unboxes, in order

    A linear walk is enough because the registration method is straight-line code: the game
    builds one control, then the next, and the action id is the nearest preceding `ldstr`.
#>
function Read-Registration([System.Reflection.MethodBase] $method, [System.Reflection.Module] $module,
                           $typeArgs, $methodArgs) {
    $result = @{
        Actions  = [ordered]@{}
        Arity    = @{}
        Unboxes  = @{}
        Captions = @{}
    }

    $il = Get-Il $method
    if (-not $il) { return $result }

    $currentId = $null
    $i = 0
    while ($i -lt $il.Length) {
        $op = $il[$i]
        $length = Get-InstructionLength $il $i

        if ($op -eq 0x72) {                       # ldstr
            $tok = [BitConverter]::ToInt32($il, $i + 1)
            try { $currentId = $module.ResolveString($tok) } catch { }
        }
        elseif ($op -eq 0x7E -and $currentId) {   # ldsfld
            # A `MyStringId` loaded here is the caption the registration hands to the generic value
            # dialog. Many actions ask for their single argument through
            # `MyGuiSandbox.ShowSetValueActionDialog(..., caption, ...)`, and that caption is loaded
            # at the registration site rather than inside the request method — so the walk over the
            # request finds nothing and the name has to be taken from here (ADR-0052 §2).
            $tok = [BitConverter]::ToInt32($il, $i + 1)
            $field = $null
            try { $field = $module.ResolveField($tok, $typeArgs, $methodArgs) } catch { }
            if ($field -and $field.FieldType -and $field.FieldType.Name -eq 'MyStringId') {
                if (-not $result.Captions.ContainsKey($currentId)) {
                    $result.Captions[$currentId] = New-Object System.Collections.ArrayList
                }
                [void]$result.Captions[$currentId].Add($field.Name)
            }
        }
        elseif ($op -eq 0xFE -and $il[$i + 1] -eq 0x06) {   # ldftn
            $tok = [BitConverter]::ToInt32($il, $i + 2)
            $target = $null
            try { $target = $module.ResolveMethod($tok, $typeArgs, $methodArgs) } catch { }
            if ($target -and $currentId) {
                if (-not $result.Actions.Contains($currentId)) {
                    $result.Actions[$currentId] = New-Object System.Collections.ArrayList
                }
                [void]$result.Actions[$currentId].Add($target)
            }
        }
        elseif ($op -eq 0xA5) {                   # unbox.any
            $tok = [BitConverter]::ToInt32($il, $i + 1)
            $name = $null
            try { $name = $module.ResolveType($tok, $typeArgs, $methodArgs).Name } catch { }
            if ($name) {
                if (-not $result.Unboxes.ContainsKey($method.MetadataToken)) {
                    $result.Unboxes[$method.MetadataToken] = New-Object System.Collections.ArrayList
                }
                [void]$result.Unboxes[$method.MetadataToken].Add($name)
            }
        }
        elseif ($op -eq 0x6F -or $op -eq 0x28) {  # callvirt / call
            $tok = [BitConverter]::ToInt32($il, $i + 1)
            $target = $null
            try { $target = $module.ResolveMethod($tok, $typeArgs, $methodArgs) } catch { }
            # The arity is the game's own declaration: one `get_ParameterDefinitions` per
            # argument, added immediately to the action's definitions list.
            if ($target -and $target.Name -eq 'get_ParameterDefinitions' -and $currentId) {
                if (-not $result.Arity.ContainsKey($currentId)) { $result.Arity[$currentId] = 0 }
                $result.Arity[$currentId] = $result.Arity[$currentId] + 1
            }
        }

        $i += $length
    }
    return $result
}

<#
    Collects the caption of every dialog the game constructs while asking for an action's
    arguments, in construction order.

    <b>This is the part that makes the names the game's own.</b> The request is not one method:
    `RequestRotationParametersAndLabel` builds the angle dialog, whose `OnConfirmed` builds the
    speed dialog, whose `OnConfirmed` builds the direction dropdown. Each dialog carries its own
    `MyStringId` caption, and that caption is the argument's name on screen. So the walk follows
    the chain — `ldftn` (a closure the dialog will call) and direct `call`/`callvirt` — at the
    point each is named, which is exactly the order the player sees the dialogs in.

    The pending caption is the nearest preceding `ldsfld` of a `MyStringId`: that is the caption
    operand the dialog's constructor receives.
#>
function Get-Dialogs([System.Reflection.MethodBase] $method, [System.Reflection.Module] $module,
                     $typeArgs, $methodArgs, [int] $depth) {
    $found = New-Object System.Collections.ArrayList
    if ($depth -gt 6) { return $found }

    $il = Get-Il $method
    if (-not $il) { return $found }

    $pending = $null
    $i = 0
    while ($i -lt $il.Length) {
        $op = $il[$i]
        $length = Get-InstructionLength $il $i

        if ($op -eq 0x7E) {                       # ldsfld
            $tok = [BitConverter]::ToInt32($il, $i + 1)
            $field = $null
            try { $field = $module.ResolveField($tok, $typeArgs, $methodArgs) } catch { }
            if ($field -and $field.FieldType -and $field.FieldType.Name -eq 'MyStringId') {
                $pending = $field.Name
            }
        }
        elseif ($op -eq 0x73) {                   # newobj
            $tok = [BitConverter]::ToInt32($il, $i + 1)
            $ctor = $null
            try { $ctor = $module.ResolveMethod($tok, $typeArgs, $methodArgs) } catch { }
            if ($ctor -and $ctor.DeclaringType -and
                $ctor.DeclaringType.Name -like 'MyGuiScreenDialog*' -and $pending) {

                # A dialog whose confirm handler sets the toolbar label is not an argument
                # dialog: every parameterised action has one, and its caption ("Set label") is
                # not the name of anything the player is being asked to type.
                $confirm = Get-ConfirmTarget $il $i $module $typeArgs $methodArgs
                if (-not (Test-SetsIconTitle $confirm)) {
                    # <b>The dialog's own class says what kind of value it collects</b>, and that
                    # is the type the action expects — it is the widget the game hands the player.
                    # This is the last resort, used only where the handler neither casts nor
                    # converts; it is still the game's own answer, not a guess.
                    $kind = $ctor.DeclaringType.Name
                    if ($kind -like 'MyGuiScreenDialogEnumDropdown*' -and $ctor.DeclaringType.IsGenericType) {
                        $enumType = $ctor.DeclaringType.GetGenericArguments()[0]
                        if ($enumType.IsEnum) {
                            $kind = 'MyGuiScreenDialogEnumDropdown:' + [Enum]::GetUnderlyingType($enumType).Name
                        }
                    }
                    [void]$found.Add(@{ Caption = $pending; Kind = $kind })
                }
            }
        }
        elseif ($op -eq 0xFE -and $il[$i + 1] -eq 0x06) {   # ldftn
            $tok = [BitConverter]::ToInt32($il, $i + 2)
            $target = $null
            try { $target = $module.ResolveMethod($tok, $typeArgs, $methodArgs) } catch { }
            if (Test-GameMethod $target) {
                foreach ($d in (Get-Dialogs $target $target.Module $null $null ($depth + 1))) {
                    [void]$found.Add($d)
                }
            }
        }
        elseif ($op -eq 0x6F -or $op -eq 0x28) {  # callvirt / call
            $tok = [BitConverter]::ToInt32($il, $i + 1)
            $target = $null
            try { $target = $module.ResolveMethod($tok, $typeArgs, $methodArgs) } catch { }

            # <b>Some actions ask through `MyGuiSandbox` instead of building a screen.</b> The
            # generic value dialog comes in two overloads and the overload IS the answer: the one
            # taking `min`/`max` as floats collects a number, the one taking `defaultText` and
            # `maxTextLength` collects text. The colour dialog collects three integers. Reading the
            # overload is reading the game's own choice of widget.
            if ($target -and $target.DeclaringType -and
                $target.DeclaringType.Name -eq 'MyGuiSandbox') {
                if ($target.Name -eq 'ShowSetColorActionDialog') {
                    [void]$found.Add(@{ Caption = $pending; Kind = 'MyGuiScreenDialogColor' })
                }
                elseif ($target.Name -eq 'ShowSetValueActionDialog') {
                    $ps = $target.GetParameters()
                    $numeric = $false
                    foreach ($prm in $ps) {
                        if ($prm.ParameterType.Name -eq 'Single' -and $prm.Name -match '^(min|max|defaultVal)$') {
                            $numeric = $true
                        }
                    }
                    $kind = if ($numeric) { 'MyGuiScreenDialogAmount' } else { 'MyGuiScreenDialogText' }
                    [void]$found.Add(@{ Caption = $pending; Kind = $kind })
                }
            }

            # A direct call is followed too: the Action Relay reaches its dialog through
            # `RequestChannelArgumentAndTitle` rather than by building it inline.
            if (Test-GameMethod $target -and $target.Name -notmatch '^(get_|set_|add_|remove_)') {
                foreach ($d in (Get-Dialogs $target $target.Module $null $null ($depth + 1))) {
                    [void]$found.Add($d)
                }
            }
        }

        $i += $length
    }
    return $found
}

<#
    The type a dialog's widget collects, or null when the dialog is not one the mapping knows.

    The dialog is the game's own value collector, so its class is the game's own statement about
    what kind of value the action takes. Used only where the handler neither casts (`unbox.any`)
    nor converts (`Convert.ToSingle`) — otherwise the handler's own cast is the better answer.
#>
function Get-DialogType([string] $kind) {
    switch ($kind) {
        'MyGuiScreenDialogText'   { return 'String' }
        'MyGuiScreenDialogAmount' { return 'Single' }
        'MyGuiScreenDialogColor'  { return 'Int32' }
        default {
            if ($kind -like 'MyGuiScreenDialogEnumDropdown:*') {
                return ($kind -split ':')[1]
            }
            return $null
        }
    }
}
<#
    True when a method, following only direct `call`/`callvirt`, ends up calling
    `SetCustomIconTitle`.

    <b>Why this matters.</b> Every parameterised action also offers a dialog for the *toolbar
    label* — the text the game shows on the item. That dialog carries a caption of its own
    ("Set label"), and its confirm handler sets the label rather than an argument. Reading it as
    an argument name is how the third rotor argument came out as "Set label" instead of "Set
    Rotation Direction". The distinguishing fact is the game's own: the label path calls
    `SetCustomIconTitle`, the argument paths do not.

    `ldftn` is deliberately **not** followed: creating a delegate does not run it, and following
    it here would make the dialog whose handler merely *builds* the label dialog look like a label
    dialog itself.
#>
function Test-SetsIconTitle([System.Reflection.MethodBase] $method) {
    # <b>Iterative and output-free on purpose.</b> A recursive version that returned a boolean
    # through several levels leaked intermediate values into the pipeline, and a non-empty array
    # is truthy in PowerShell — so "does this reach SetCustomIconTitle" answered "yes" for every
    # dialog. The queue keeps one return path, and there is exactly one value on the pipeline.
    #
    # Only direct `call`/`callvirt` are followed: `ldftn` merely creates a delegate, and following
    # it would make the dialog whose handler *builds* the label dialog look like a label dialog.
    if (-not $method) { return $false }

    $pending = New-Object System.Collections.Queue
    $pending.Enqueue($method)
    $seen = New-Object 'System.Collections.Generic.HashSet[int]'
    $steps = 0

    while ($pending.Count -gt 0 -and $steps -lt 400) {
        $steps++
        $m = $pending.Dequeue()
        if (-not $m) { continue }
        if (-not $seen.Add($m.MetadataToken)) { continue }

        $il = Get-Il $m
        if (-not $il) { continue }

        $i = 0
        while ($i -lt $il.Length) {
            $op = $il[$i]
            $length = Get-InstructionLength $il $i

            if ($op -eq 0x28 -or $op -eq 0x6F) {
                $tok = [BitConverter]::ToInt32($il, $i + 1)
                $target = $null
                try { $target = $m.Module.ResolveMethod($tok) } catch { }
                if ($target) {
                    if ($target.Name -eq 'SetCustomIconTitle') { return $true }
                }
            }

            $i += $length
        }
    }
    return $false
}
<#
    The method a dialog's `OnConfirmed` is wired to, or null.

    The pattern the game emits is `newobj <dialog>; dup; …; ldftn <handler>; newobj Action…;
    callvirt add_OnConfirmed`, so the answer is the last `ldftn` before the next
    `add_OnConfirmed` in the same method.
#>
function Get-ConfirmTarget([byte[]] $il, [int] $at, [System.Reflection.Module] $module, $typeArgs, $methodArgs) {
    $candidate = $null
    $i = $at
    while ($i -lt $il.Length) {
        $op = $il[$i]
        $length = Get-InstructionLength $il $i

        if ($op -eq 0xFE -and $il[$i + 1] -eq 0x06) {        # ldftn
            $tok = [BitConverter]::ToInt32($il, $i + 2)
            try { $candidate = $module.ResolveMethod($tok, $typeArgs, $methodArgs) } catch { }
        }
        elseif ($op -eq 0x6F -or $op -eq 0x28) {             # callvirt / call
            $tok = [BitConverter]::ToInt32($il, $i + 1)
            $target = $null
            try { $target = $module.ResolveMethod($tok, $typeArgs, $methodArgs) } catch { }
            # Only `add_OnConfirmed` ends the search. `add_OnCancelled` must NOT: the label
            # dialog wires cancel first and confirm second, and aborting on the cancel wiring
            # made every label dialog look like it had no confirm handler at all — which is how
            # "Set label" ended up offered as the third rotor argument.
            if ($target -and $target.Name -eq 'add_OnConfirmed') { return $candidate }
        }

        $i += $length
    }
    return $candidate
}

<#
    The types an action's handler reads its arguments as.

    Most handlers cast straight from the captured value — `unbox.any Single` — and that is the
    whole answer. Some delegate the cast to a helper instead: `OnSetBlinkIntervalApplied` calls
    `TryGetFloatFromCapturedParam`, which is where the `unbox.any Single` lives. Reading only the
    handler would leave those actions with no type at all, and a value written without a type is
    the dead connection this whole decision exists to prevent.

    So the handler is walked first, and only if it casts nothing itself are the game methods it
    calls walked (two levels, still only game assemblies). The result is truncated to the game's
    own arity by the caller.
#>
function Get-HandlerTypes([System.Reflection.MethodBase] $handler, [int] $depth) {
    $types = New-Object System.Collections.ArrayList
    if (-not $handler -or $depth -gt 2) { return $types }

    $il = Get-Il $handler
    if (-not $il) { return $types }

    $calls = New-Object System.Collections.ArrayList
    $i = 0
    while ($i -lt $il.Length) {
        $op = $il[$i]
        $length = Get-InstructionLength $il $i

        if ($op -eq 0xA5) {                       # unbox.any
            $tok = [BitConverter]::ToInt32($il, $i + 1)
            $name = $null
            try { $name = $handler.Module.ResolveType($tok).Name } catch { }
            if ($name) { [void]$types.Add($name) }
        }
        elseif ($op -eq 0x6F -or $op -eq 0x28) {  # callvirt / call
            $tok = [BitConverter]::ToInt32($il, $i + 1)
            $target = $null
            try { $target = $handler.Module.ResolveMethod($tok) } catch { }
            if (Test-GameMethod $target -and $target.Name -notmatch '^(get_|set_|add_|remove_)') {
                [void]$calls.Add($target)
            }
            # Some handlers convert instead of casting: `OnSetDelay` calls `Convert.ToSingle`, and
            # `OnSetHudText` calls `Object.ToString` because the HUD text is whatever the captured
            # value prints as. The conversion names the type the action expects, and it is the
            # game's own choice of conversion.
            # A handler that prints the captured value (`Object.ToString`) takes text: the beacon's
            # HUD text is whatever the value reads as, which is how the game stores it too.
            if ($target -and $target.Name -eq 'ToString' -and $target.DeclaringType -and
                $target.DeclaringType.FullName -eq 'System.Object') {
                [void]$types.Add('String')
            }
            if ($target -and $target.DeclaringType -and $target.DeclaringType.FullName -eq 'System.Convert') {
                switch ($target.Name) {
                    'ToSingle'  { [void]$types.Add('Single') }
                    'ToDouble'  { [void]$types.Add('Double') }
                    'ToInt32'   { [void]$types.Add('Int32') }
                    'ToInt64'   { [void]$types.Add('Int64') }
                    'ToBoolean' { [void]$types.Add('Boolean') }
                    'ToString'  { [void]$types.Add('String') }
                }
            }
        }

        $i += $length
    }

    if ($types.Count -gt 0 -or $depth -ge 2) { return $types }

    foreach ($c in $calls) {
        foreach ($t in (Get-HandlerTypes $c ($depth + 1))) {
            if ($types -notcontains $t) { [void]$types.Add($t) }
        }
    }
    return $types
}

<#
    True when a handler looks inside the parameter list at all.

    A parameterised action receives a `ListReader<TerminalActionParameter>`; a handler that reads
    it calls `get_Count`, `get_Item`, `get_IsEmpty` or `FirstOrDefault` on it. One that does none
    of those takes the argument because the game requires the registration, not because it uses
    it — and the value written for it can be the game's own empty one.
#>
<#
    The parameter type of the single-argument setter a handler feeds the captured value to, or
    null.

    A handler that neither casts nor converts still has to put the value somewhere, and where it
    puts it says what it is: `MyBeacon.SetHudText(String)` is a text setter, and the game's own
    signature is the statement that the HUD text is text. Only one-parameter `Set*`/`set_*`
    methods count — anything else is not "the value goes here".
#>
function Get-SetterParameterType([System.Reflection.MethodBase] $handler) {
    $il = Get-Il $handler
    if (-not $il) { return $null }

    $i = 0
    while ($i -lt $il.Length) {
        $op = $il[$i]
        $length = Get-InstructionLength $il $i

        if ($op -eq 0x6F -or $op -eq 0x28) {
            $tok = [BitConverter]::ToInt32($il, $i + 1)
            $target = $null
            try { $target = $handler.Module.ResolveMethod($tok) } catch { }
            if ($target -and $target.Name -match '^(Set|set_)' -and (Test-GameMethod $target)) {
                $ps = $target.GetParameters()
                if ($ps.Count -eq 1) {
                    $name = $ps[0].ParameterType.Name
                    if ($script:simpleTypes -contains $name) { return $name }
                }
            }
        }

        $i += $length
    }
    return $null
}

function Test-ReadsArguments([System.Reflection.MethodBase] $handler) {
    $il = Get-Il $handler
    if (-not $il) { return $true }

    $i = 0
    while ($i -lt $il.Length) {
        $op = $il[$i]
        $length = Get-InstructionLength $il $i

        if ($op -eq 0x6F -or $op -eq 0x28) {
            $tok = [BitConverter]::ToInt32($il, $i + 1)
            $target = $null
            try { $target = $handler.Module.ResolveMethod($tok) } catch { }
            if ($target -and $target.Name -match '^(get_Count|get_Item|get_IsEmpty|FirstOrDefault)$') {
                return $true
            }
        }

        $i += $length
    }
    return $false
}

function Is-Handler($m) {
    if (-not $m) { return $false }
    $ps = $m.GetParameters()
    return ($ps.Count -eq 2 -and $ps[1].ParameterType.Name -like 'ListReader*')
}

function Is-Request($m) {
    if (-not $m) { return $false }
    $ps = $m.GetParameters()
    return ($ps.Count -eq 2 -and $ps[0].ParameterType.Name -eq 'IUserCustomizableTerminalAction')
}

# ---------------------------------------------------------------------------- the scan

$actions = [ordered]@{}
$typeCodes = [Enum]::GetNames([System.TypeCode])
$script:simpleTypes = @("String", "Single", "Double", "Int32", "Int64", "Boolean")

foreach ($file in $gameAssemblies) {
    $path = Join-Path $Bin $file
    if (-not (Test-Path $path)) { continue }
    $asm = [System.Reflection.Assembly]::LoadFrom($path)
    $module = $asm.ManifestModule
    $types = try { $asm.GetTypes() } catch { $_.Exception.Types | Where-Object { $_ } }

    foreach ($t in $types) {
        if (-not $t) { continue }
        $typeArgs = if ($t.IsGenericTypeDefinition) { $t.GetGenericArguments() } else { $null }
        $flags = [System.Reflection.BindingFlags]'Static,Public,NonPublic,Instance,DeclaredOnly'
        $methods = try { $t.GetMethods($flags) } catch { continue }

        foreach ($m in $methods) {
            if ($m.Name -notmatch 'CreateTerminalControls') { continue }
            $methodArgs = if ($m.IsGenericMethodDefinition) { $m.GetGenericArguments() } else { $null }
            $scan = Read-Registration $m $module $typeArgs $methodArgs
            if ($scan.Actions.Count -eq 0) { continue }

            foreach ($id in $scan.Actions.Keys) {
                if ($actions.Contains($id)) { continue }

                $handler = $null
                $request = $null
                foreach ($h in $scan.Actions[$id]) {
                    if (Is-Handler $h) { $handler = $h }
                    elseif (Is-Request $h) { $request = $h }
                }

                # No parameterised handler means the action takes no arguments, whatever it
                # registered: `MyTerminalAction` also exists in a zero-argument form.
                if (-not $handler) { continue }

                $arity = 0
                if ($scan.Arity.ContainsKey($id)) { $arity = $scan.Arity[$id] }

                # Types: what the handler casts each argument to, truncated to the game's arity.
                # A handler also unboxes its own enums and structures, and a nested lambda may
                # unbox a copy of an argument for a network event; the arity is what separates
                # "the argument" from "the same value read twice".
                $typesOut = @()

                # Only real TypeCode members are candidates. A handler also unboxes its own enums
                # and structures — `OnAction` on a remote control unboxes a `Direction` while
                # filling the GPS list — and those are not argument types. Filtering here, before
                # the fallbacks, is what lets a handler whose only unbox was its own enum fall
                # through to "this action does not read its argument".
                $derived = @()
                foreach ($u in (Get-HandlerTypes $handler 0)) {
                    if ($typeCodes -notcontains $u) { continue }
                    if ($u -eq 'Empty' -or $u -eq 'Object' -or $u -eq 'DBNull') { continue }
                    $derived += $u
                }

                # Two fallbacks, in the order of how directly the game states the answer.
                #
                # 1. The handler passes the captured value straight into a setter, and that
                #    setter's parameter is the type the action expects: `OnSetHudText` calls
                #    `MyBeacon.SetHudText(String)`. This is the game's own signature, not a guess.
                #
                # 2. An action can declare an argument its handler never reads — it registers a
                #    parameter and then runs a handler that looks at none of it. The value is still
                #    required for the toolbar item to be well formed, and the game's own empty
                #    parameter is a String, so that is what is written. Derived, not assumed: a
                #    handler that never touches the parameter list is exactly a handler whose
                #    argument does not matter.
                #
                # A third source — the widget of the dialog the request builds — was tried and is
                # deliberately not used: for the one action that would need it the overload the
                # request resolves to does not distinguish text from number reliably, and a wrong
                # type is worse than an honest gap. That action stays unknown and the coverage log
                # names it.
                if ($derived.Count -eq 0) {
                    $setter = Get-SetterParameterType $handler
                    if ($setter) { $derived = @($setter) }
                }
                if ($derived.Count -eq 0 -and -not (Test-ReadsArguments $handler)) {
                    $derived = @('String')
                }

                # One dialog, several parameters, one type: a colour is three integers behind a
                # single picker, and the handler may cast the first and convert the rest, or cast
                # one and let the dialog supply the other two. Repeating the derived type across
                # the group is the dialog's own shape, not a guess.
                if ($derived.Count -eq 1 -and $arity -gt 1) {
                    $derived = @(for ($k = 0; $k -lt $arity; $k++) { $derived[0] })
                }

                foreach ($u in $derived) {
                    if ($typeCodes -notcontains $u) { continue }
                    $typesOut += $u
                }
                if ($arity -gt 0 -and $typesOut.Count -gt $arity) {
                    $typesOut = @($typesOut[0..($arity - 1)])
                }

                # Captions: one per dialog the request builds, in the order it builds them,
                # truncated to the same arity.
                $captionsOut = @()
                $dialogTypes = @()
                if ($request) {
                    foreach ($d in (Get-Dialogs $request $request.Module $null $null 0)) {
                        $captionsOut += $d.Caption
                        $dialogTypes += (Get-DialogType $d.Kind)
                    }
                }
                # The dialogs the request builds are the first choice: they are one per question the
                # player is asked. Only when the request builds none — because it is the generic
                # value dialog and its caption is loaded at the registration site — does the
                # registration's own caption stand in.
                if ($captionsOut.Count -eq 0 -and $scan.Captions.ContainsKey($id)) {
                    $captionsOut = @($scan.Captions[$id])
                }
                if ($arity -gt 0 -and $captionsOut.Count -gt $arity) {
                    $captionsOut = @($captionsOut[0..($arity - 1)])
                }

                # How many toolbar parameters each dialog fills.
                #
                # <b>One dialog does not always mean one parameter.</b> `SetColor` declares three
                # integer parameters but asks for them with a single colour picker, so its one
                # caption covers all three; a rotor's `RotateToAngle` asks three separate questions
                # and covers one each. The split is read off the two counts the game gives us, not
                # guessed: equal counts mean one each, one caption for several slots means that
                # caption names the whole value. Rendering the colour as three fields would force
                # two invented names — the placeholder this decision exists to remove.
                $groupSizes = @()
                if ($captionsOut.Count -gt 0 -and $arity -gt 0) {
                    if ($captionsOut.Count -eq $arity) {
                        for ($g = 0; $g -lt $arity; $g++) { $groupSizes += 1 }
                    }
                    elseif ($captionsOut.Count -eq 1) {
                        $groupSizes += $arity
                    }
                    else {
                        for ($g = 0; $g -lt $captionsOut.Count; $g++) {
                            $groupSizes += if ($g -eq $captionsOut.Count - 1) {
                                $arity - ($captionsOut.Count - 1)
                            } else { 1 }
                        }
                    }
                }

                $actions[$id] = [ordered]@{
                    Id         = $id
                    Block      = $t.Name
                    Arity      = $arity
                    Types      = $typesOut
                    Captions   = $captionsOut
                    GroupSizes = $groupSizes
                }
            }
        }
    }
}

# ---------------------------------------------------------------------------- names

foreach ($id in @($actions.Keys)) {
    $a = $actions[$id]
    $namesEn = @()
    $namesRu = @()
    foreach ($c in $a.Captions) {
        $en = if ($english.ContainsKey($c)) { $english[$c] } else { $null }
        $ru = if ($russian.ContainsKey($c)) { $russian[$c] } else { $en }
        $namesEn += $en
        $namesRu += $ru
    }
    $a['NamesEn'] = $namesEn
    $a['NamesRu'] = $namesRu
}

$report = @($actions.Values)

if (-not $EmitTable) {
    Write-Output "PARAMETERISED ACTIONS: $($report.Count)"
    foreach ($a in $report) {
        $types = if ($a.Types.Count -gt 0) { $a.Types -join ', ' } else { '(none)' }
        Write-Output ("  {0,-28} {1,-32} arity {2}  types: {3}" -f $a.Id, $a.Block, $a.Arity, [string]$types)
        if ($a.NamesEn.Count -eq 0) {
            Write-Output ("  {0,-28} {1,-32}         names: (none)" -f '', '')
        } else {
            for ($i = 0; $i -lt $a.NamesEn.Count; $i++) {
                Write-Output ("  {0,-28} {1,-32}         name {2}: {3}" -f '', '', ($i + 1), $a.NamesEn[$i])
            }
        }
    }
    if ($Out) { ConvertTo-Json -InputObject $report -Depth 6 | Set-Content -Path $Out -Encoding utf8 }
    return
}

# ---------------------------------------------------------------------------- C# table

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('// <auto-generated>')
[void]$sb.AppendLine('//     Generated by tools/scan-action-parameters.ps1 from the installed game assemblies.')
[void]$sb.AppendLine('//     Do not edit: run the script and let the offline gate verify the result (ADR-0052).')
[void]$sb.AppendLine('// </auto-generated>')
[void]$sb.AppendLine()
[void]$sb.AppendLine('using System;')
[void]$sb.AppendLine('using System.Collections.Generic;')
[void]$sb.AppendLine()
[void]$sb.AppendLine('namespace Vgs.Model')
[void]$sb.AppendLine('{')
[void]$sb.AppendLine('    /// <summary>')
[void]$sb.AppendLine('    /// The arguments of every terminal action that declares them, as the game itself defines')
[void]$sb.AppendLine('    /// them — the derived half of ADR-0052.')
[void]$sb.AppendLine('    ///')
[void]$sb.AppendLine('    /// <b>Generated, not written.</b> For each argument the game builds a dialog whose caption')
[void]$sb.AppendLine('    /// is that argument''s name, and the chain of methods building those dialogs is reachable')
[void]$sb.AppendLine('    /// from the action''s registration; the type is the `unbox.any` its handler performs. Both')
[void]$sb.AppendLine('    /// are read from the assemblies by tools/scan-action-parameters.ps1, and the offline gate')
[void]$sb.AppendLine('    /// re-runs that script and fails if this file no longer matches. A hand-edited table would')
[void]$sb.AppendLine('    /// be a guess; this one cannot drift from the game without the gate saying so.')
[void]$sb.AppendLine('    /// </summary>')
[void]$sb.AppendLine('    internal static class ActionParameterTable')
[void]$sb.AppendLine('    {')
[void]$sb.AppendLine('        /// <summary>One action: the type of each argument and the caption of its dialog.</summary>')
[void]$sb.AppendLine('        internal sealed class Row')
[void]$sb.AppendLine('        {')
[void]$sb.AppendLine('            internal readonly TypeCode[] Types;')
[void]$sb.AppendLine('            internal readonly string[] NamesEn;')
[void]$sb.AppendLine('            internal readonly string[] NamesRu;')
[void]$sb.AppendLine()
[void]$sb.AppendLine('            /// <summary>')
[void]$sb.AppendLine('            /// How many toolbar parameters each entry of NamesEn fills, in order. All ones')
[void]$sb.AppendLine('            /// for an action that asks one question per argument; a single entry larger than')
[void]$sb.AppendLine('            /// one where the game asks for a whole value at once (a colour is three integers')
[void]$sb.AppendLine('            /// behind one picker).')
[void]$sb.AppendLine('            /// </summary>')
[void]$sb.AppendLine('            internal readonly int[] GroupSizes;')
[void]$sb.AppendLine()
[void]$sb.AppendLine('            internal Row(TypeCode[] types, string[] namesEn, string[] namesRu, int[] groupSizes)')
[void]$sb.AppendLine('            {')
[void]$sb.AppendLine('                Types = types;')
[void]$sb.AppendLine('                NamesEn = namesEn;')
[void]$sb.AppendLine('                NamesRu = namesRu;')
[void]$sb.AppendLine('                GroupSizes = groupSizes;')
[void]$sb.AppendLine('            }')
[void]$sb.AppendLine('        }')
[void]$sb.AppendLine()
[void]$sb.AppendLine('        private static readonly Dictionary<string, Row> Rows = Build();')
[void]$sb.AppendLine()
[void]$sb.AppendLine('        /// <summary>The row for one action id, or null when it declares no arguments.</summary>')
[void]$sb.AppendLine('        internal static Row Get(string actionId)')
[void]$sb.AppendLine('        {')
[void]$sb.AppendLine('            if (string.IsNullOrEmpty(actionId)) return null;')
[void]$sb.AppendLine('            Row row;')
[void]$sb.AppendLine('            return Rows.TryGetValue(actionId, out row) ? row : null;')
[void]$sb.AppendLine('        }')
[void]$sb.AppendLine()
[void]$sb.AppendLine('        /// <summary>Every action the table describes, for the coverage report (ADR-0052 §4).</summary>')
[void]$sb.AppendLine('        internal static IEnumerable<string> Ids')
[void]$sb.AppendLine('        {')
[void]$sb.AppendLine('            get { return Rows.Keys; }')
[void]$sb.AppendLine('        }')
[void]$sb.AppendLine()
[void]$sb.AppendLine('        private static Dictionary<string, Row> Build()')
[void]$sb.AppendLine('        {')
[void]$sb.AppendLine('            var rows = new Dictionary<string, Row>(' + $report.Count + ', StringComparer.Ordinal);')

foreach ($a in $report) {
    if ($a.Types.Count -eq 0) { continue }

    $typeList = ($a.Types | ForEach-Object { 'TypeCode.' + $_ }) -join ', '
    # An empty list is written as an explicitly typed empty array: an implicitly typed
    # `new[] { }` has no element type and does not compile.
    $en = ($a.NamesEn | ForEach-Object { if ($null -eq $_) { 'null' } else { '"' + ($_ -replace '\\', '\\\\' -replace '"', '\"') + '"' } }) -join ', '
    $ru = ($a.NamesRu | ForEach-Object { if ($null -eq $_) { 'null' } else { '"' + ($_ -replace '\\', '\\\\' -replace '"', '\"') + '"' } }) -join ', '
    $enExpr = if ($en) { 'new[] { ' + $en + ' }' } else { 'new string[0]' }
    $ruExpr = if ($ru) { 'new[] { ' + $ru + ' }' } else { 'new string[0]' }
    $grp = ($a.GroupSizes -join ', ')
    $grpExpr = if ($grp) { 'new[] { ' + $grp + ' }' } else { 'new int[0]' }

    [void]$sb.AppendLine('            rows["' + $a.Id + '"] = new Row(')
    [void]$sb.AppendLine('                new[] { ' + $typeList + ' },')
    [void]$sb.AppendLine('                ' + $enExpr + ',')
    [void]$sb.AppendLine('                ' + $ruExpr + ',')
    [void]$sb.AppendLine('                ' + $grpExpr + ');')
}

[void]$sb.AppendLine('            return rows;')
[void]$sb.AppendLine('        }')
[void]$sb.AppendLine('    }')
[void]$sb.AppendLine('}')

if ($Out) {
    Set-Content -Path $Out -Value $sb.ToString() -Encoding utf8
    Write-Output "Wrote $($report.Count) action(s) to $Out"
} else {
    Write-Output $sb.ToString()
}


























