---
name: vgs-programmer
description: Use when writing or changing C# code for the VGS Space Engineers mod — adding mod sources, fixing a compile error against the game assemblies, wiring terminal controls, drawing an in-game screen, or debugging a runtime exception from the game log. Covers the build/verify loop and the Space Engineers API traps that cost the most time.
---

# VGS programmer

You write the C# mod. This skill is the checklist plus the traps; the code lives in
`src/VGS.Mod/Data/Scripts/VGS/`.

## 1. The loop

```powershell
# Type-check against the real game assemblies. This is the primary gate.
pwsh -File tools/verify-compile.ps1

# Static quality gate: layering, C# 7.3, guards, draw-path allocation.
pwsh -File tools/review-check.ps1

# Behaviour gate: graph invariants, port slots, card/port geometry, wire routing.
# Runs without starting the game; a failure names the property that broke.
pwsh -File tools/offline-check.ps1

# Deploy to the game's local mod folder for an in-game run.
pwsh -File tools/deploy-mod.ps1

# Watch the mod's own log lines while testing.
pwsh -File tools/tail-mod-log.ps1
```

`verify-compile.ps1` uses the same Roslyn family the game uses, with `-nostdlib+` and .NET
Framework 4.x references, so an overload that resolves here resolves in game. A green run
is necessary, not sufficient: it proves the code is *callable*, not that it behaves.

`offline-check.ps1` is where behaviour is actually asserted. If you change `GridGraph`,
`PortGeometry` or `EdgeRouting`, add an assertion to `tests/OfflineChecks.cs` in the same
change. It is a plain `Main` with comparisons — no framework, no discovery — so the cost of
adding one is a few lines.

`review-check.ps1` encodes the mechanical half of code review. Run it before handing the
change over — a finding you fix yourself is cheaper than one the reviewer sends back.

`review-check.ps1` reads code with string literals and comments stripped before applying the
syntax rules (R002, R003). A sentence in a user-facing string is not C#: without that, the text
`"the player is not in the world yet"` tripped R003's `is not` pattern (C# 9) and failed the gate
on a string that cannot be a syntax error. When a rule fires on text, the gate is wrong — do not
suppress the rule on that line.

### Strings: a table of data, a facade of properties

User-facing text lives in `Core/VgsStrings.cs` as two `key → value` dictionaries (`English`,
`Russian`), read through `VgsText` properties (ADR-0033). A new string is a key plus a value in
both tables and one property on the facade. Three things that are easy to get wrong:

- **Completeness is a gate, not a compile error.** Nothing in the compiler stops you adding a key
  to one table only. `offline-check.ps1` proves the key sets match, that every value formats, that
  the placeholders `{n}` agree between languages, and that no property resolves to a key. A red
  `OFFLINE` here is the only thing standing between you and `CLOSE_BUTTON` on screen.
- **The fallback is silent.** `VgsText.S(key)` tries the active language, then English, then the
  key itself. That chain is why a missing translation degrades instead of throwing in a draw
  path — and why a typo'd key looks like a bug rather than an exception. The gate is what
  notices.
- **Do not translate what is not a sentence.** Fallback glyphs (`+`, `T`, `F`, `#`, `>`, `=`, `x`)
  and number formats stay constants in `VgsText`: a glyph from another alphabet is the likeliest
  one to be missing from a fallback font, and `0,5` is a different number to the game.

## 2. Structure you must respect

```
Vgs.Core/   VgsLog, VgsSession, VgsConstants, VgsText (facade), VgsStrings (tables)
                                                         — no game logic
Vgs.Model/  Node, Port, Edge, GridGraph, BlockPortCatalog, ToolbarPlanner
                                                         — no Sandbox.* references
Vgs.Game/   GridGraphBuilder, ToolbarProbe, ToolbarWriter, VgsTerminalControls,
            GameLanguage                                 — the only world access
Vgs.Ui/     VgsEditorScreen, PortGeometry, EdgeRouting, VgsScreenDraw
                                                         — draws cached data only
```

Adding a `using Sandbox.ModAPI;` to `Vgs.Model` is a layering violation, not a shortcut.
The model layer is the only part that can be reasoned about without launching the game.

Card and port rectangles come from `PortGeometry` only. If you compute one in the draw pass
and again in the hit test, the two will drift and the port will be clickable somewhere other
than where it is drawn. Same for menu rows: `GetMenuRect`/`GetMenuRowRect` serve both.

Port bars are a *derived* view of the edges — `GridGraph.LayoutPorts()` decides which ports
are drawn. Call it after every change to the edges, never from the UI.

## 2a. Toolbars: the block is the storage

A connection is an item in the source block's real toolbar (ADR-0011). Three files carry that
and nothing else should touch it:

| File | Role | Called from |
|---|---|---|
| `Vgs.Game/ToolbarProbe.cs` | finds the `IMyToolbarProvider` component, reads `IMyToolbar` → `ToolbarLayout` (slots, pages, yes/no) | refresh only |
| `Vgs.Model/ToolbarPlanner.cs` | pure placement: which cell, or why there is none | writer and offline checks |
| `Vgs.Game/ToolbarWriter.cs` | `BeginChanges().Set(...).Apply()` + read-back; `TryClear` | the click handler only |

Rules that bite when ignored:

- **Write with `toolbar.BeginChanges().Set(index, MyToolbarItemInfo.BlockAction(blockId, actionId)).Apply()`.**
  `Apply()` returns `false` when this machine does not own the block's toolbar
  (`MyToolbar.SendChanges` → `IsOwnedHere` → `Sync.IsServer`): single player and the host can
  write, a remote multiplayer client cannot. Report that instead of claiming a connection.
- **Never touch `MyToolbar`, `IMyToolbarOwner` or `MyToolbarItemFactory`.** They live in
  `Sandbox.Game.Screens.Helpers`, which the IL whitelist does not allow — `Sandbox.Game` is
  allowed *exactly*, not as a prefix. Using them compiles locally and kills the mod at world
  load with "The type or member 'MyToolbar' is prohibited". The ModAPI mirror
  (`IMyToolbarProvider`, `IMyToolbar`, `IMyToolbarSlots`, `MyToolbarItemInfo`) is the way in.
- **Read the cell back** with `TryGetItem`. The game can refuse an item, and `Kind` must be
  `MyToolbarItemKind.BlockAction` for the cell to mean "block + action".
- **Never hardcode slot or page counts.** A timer (9×10), a sensor (2×10), an event controller
  (2×10) and a cockpit's on-lock toolbar (2×10) disagree, and a block from another mod can
  disagree again. `ToolbarLayout` is read from the block.
- **"Yes" is slot 0, "no" is slot 1** of a two-slot toolbar. A `PortGroup` is part of the port
  key (`Detected#true`), so a saved connection remembers which list it joined. The yes/no flag
  is not on `IMyToolbar`; read `MyCubeBlockDefinition.YesNoToolbarBackground`.

### Groups are a second item kind, not a second mechanism

A connection may end on a named block group instead of a block (ADR-0015). The item is
`MyToolbarItemInfo.GroupAction(contextBlockId, groupName, actionId)` — `ToolbarWriter.TryWriteGroup` —
and `ToolbarWriter.CellContents` is what tells the two kinds apart when reading back. Things that
bite:

- **A group has no entity id.** Its identity is the name, so a rename is a different group and the
  connection stops resolving. Never key a group by `EntityId` (it is zero), never assume
  `GetNode(edge.TargetEntityId)` finds it — use `GridGraph.GetTargetNode(edge)`.
- **A group is a target only.** It has no toolbar, so it has no output ports and `CanDriveBlocks`
  is false. Do not "fix" that by giving it one.
- **The action list is the game's rule, not a union:** one type → that type's actions; mixed types
  and all members functional → intersection of action ids; mixed types with a non-functional member
  → nothing. `BlockPortCatalog.ComputeGroupActionIds` is the single implementation, and it is
  asserted offline — do not compute a second version anywhere else.
- **Visibility aggregates.** The group node exists only while at least one member has a node; that
  is the whole of "Show in VGS" for a group. Do not add a flag to the group.
- **The action list is computed from the whole group**, including hidden members and members on
  subgrids — the game applies the action to all of them.

## 3. API traps that cost the most time

Each of these was hit while building the skeleton.

**`netstandard.dll` is a required reference.** The game assemblies are built against
`netstandard`, so omitting it produces hundreds of bogus *"The type 'Object' is defined in
an assembly that is not referenced"* errors. `verify-compile.ps1` already includes it.

**`MyObjectBuilder_SessionComponent` lives in `VRage.Game`, not `VRage.Game.Components`.**
`using VRage.Game;` is required.

**`MyGuiScreenBase` has no parameterless constructor.** The only reachable one is
`protected MyGuiScreenBase(Vector2? position, Vector4? backgroundColor, Vector2? size,
bool isTopMostScreen, string backgroundTexture, float backgroundTransition,
float guiTransition, MyGuiSlot? gamepadSlot)`. Named arguments are the readable way to call it.

**`IMyTerminalBlock.BlockDefinition` is a `SerializableDefinitionId` struct, not a reference.**
`!= null` does not compile. Check `string.IsNullOrEmpty(block.BlockDefinition.SubtypeName)`.

**`IMyPlayer.PlayerID` is obsolete**; use `IdentityId` (a `long`) and pass it to
`HasPlayerAccess(long, ...)`.

**`MyScreenManager` exposes only a debug *string* dump of the screen stack**, not the
screens. Track your own open screen in a static field instead of searching for it.

**`MyScreenSpace*` coordinates are the safe-GUI area, not the window.** 0..1 spans
`min(W, 4H/3)` centred horizontally (a 4:3 box), full height; the game computes it in
`MyGuiManager.UpdateScreenSize` from `m_safeGuiRectangle`. Drawing 0..1 therefore leaves bars
on a 16:9/16:10 monitor. `VgsScreenDraw` converts client-area coordinates to that space via
`MyScreenSpace.GetSafeGuiArea()`; do not call `MyScreenSpace*` around it. `TryGetCursorPosition`
returns the cursor in the same safe-GUI space, so it needs the inverse conversion. See
[ADR-0009](../../docs/adr/0009-screen-coordinate-space.md).

**`MyScreenSpaceText.Measure` returns a *normalized* size, not pixels.** It ends in
`GetNormalizedSizeFromScreenSize`, i.e. the value is already divided by the safe-GUI size.
`MeasurePixels` is the pixel variant. Applying `PixelSizeToNormalized` to `Measure` shrinks the
result by the safe width — that is how right-aligned text ended up outside the panel.

**There is no line and no circle primitive.** `MyScreenSpaceSprite.Draw` takes `rotation`, but
the pivot semantics of `DrawPivoted` are not documented for mods, so a rotated quad is a guess
that rotates everything around the wrong point. `VgsScreenDraw.Segment` draws an axis-aligned
segment as a rectangle; `VgsScreenDraw.Disc` draws a circle as a stack of slabs. Both are
clipped by intersecting with the canvas rectangle rather than by
`MyScreenSpace.UsingScissorRectangle`: the scissor pushes and pops state into a shared queue,
and getting the nesting wrong makes the game log "left N scissor rectangle(s) open".

**`MyScreenSpaceText.Draw`'s `maxWidth` is in the same normalized units as the position.** Pass
it through `_apiScaleX` like a size, not like a point, or every clipped label is cut at
`1/scaleX` of its box.

**Terminal controls must be registered after the block definitions exist.** Registering in
`Init()` silently loses the control on some load orders. `VgsSessionComponent` retries in
`UpdateAfterSimulation` — keep that pattern.

**Always unregister global hooks in `UnloadData`.** Leaving `CustomControlGetter` attached
keeps a delegate pointing at a dead session component after the player returns to the main
menu, and the next session throws on it.

**The engine's toolbar types are prohibited to a mod.** `MyToolbar`, `IMyToolbarOwner` and
`MyToolbarItemFactory` live in `Sandbox.Game.Screens.Helpers`; the IL whitelist allows
`Sandbox.Game` exactly and does not cover its sub-namespaces, so a reference compiles locally
and then fails at world load with *"The type or member 'MyToolbar' is prohibited"* — the whole
mod stops loading. Use the ModAPI mirror instead: `VRage.Game.ModAPI.IMyToolbarProvider`,
`IMyToolbar`, `IMyToolbarSlots`, `IMyToolbarChangeBuilder`, `MyToolbarItemInfo`,
`MyToolbarItemKind` (all whitelisted). `MyToolbarType` (`VRage.Game`) and
`Sandbox.Definitions.MyCubeBlockDefinition` (for the yes/no flag) are allowed.

**The toolbar component is found by interface, not by class name.** The engine registers
`Sandbox.Game.EntityComponents.MyToolbarProviderComponent`, but a mod must not name it: walk
`((MyEntity)block).Components` and take the first `IMyToolbarProvider`. A block without that
component cannot drive anything.

**`Apply()` does not write on a remote multiplayer client.** `IMyToolbarChangeBuilder.Apply`
ends in `MyToolbar.SendChanges`, which returns `false` unless `IsOwnedHere` — for a block
toolbar that means `Sync.IsServer`. Single player and the host write; a client's write is
refused. `Apply()` reports it honestly, so surface the failure rather than assuming success.
Reading works everywhere.

**`IMyToolbarSlots.ItemCount` is `SlotCount * PageCount`, and the index is linear.**
`index = page * SlotCount + slot`. `SlotCount`/`PageCount` differ per block: read them, never
assume. A two-slot toolbar *whose block definition sets* `YesNoToolbarBackground` has slot 0 =
"yes" and slot 1 = "no", fired by different events. `IMyToolbar` does not expose that flag —
read it from `MyCubeBlockDefinition.YesNoToolbarBackground`.

**The vanilla toolbar screen deletes duplicates on the current page.** `DropGridItemToToolbar`
removes every other slot of the page holding an equal item before it places the new one, so a
duplicate written next to its twin is silently lost. Put repeats on another page.

**`MyToolbarItemInfo` is a struct.** `TryGetItem` needs an `out` variable, and `Kind` must be
`MyToolbarItemKind.BlockAction` for the cell to describe a block-plus-action connection.

## 4. Defensive rules for game-facing code

A mod exception can break the whole session update, so every entry point from the game is a
boundary:

```csharp
public override void UpdateAfterSimulation()
{
    try { /* work */ }
    catch (Exception e) { VgsLog.Error("update failed", e); }
}
```

- Guard every `IMyTerminalBlock`/`IMyCubeGrid` read: the entity can be destroyed between
  frames, and `EntityId` values can be stale.
- Re-validate with `MyAPIGateway.Entities.TryGetEntityById` before using a cached entity.
- Never allocate in `Draw()` or a per-frame update. No LINQ, no closures, no
  `string.Format` in a loop; cache the formatted string and rebuild only on change.
- Log through `VgsLog` (`Info`/`Warn`/`Error`/`Throttled`/`Debug`), never
  `MyLog.Default.WriteLine` directly. `Throttled` exists for conditions that can be true
  every frame.
- Never `catch { }` around game calls that matter; catch, log once, and degrade.

## 5. Comment standard

Say *why*, not *what*. The code already says what it does.

```csharp
// Bad: increments the frame counter
Frame++;

// Good: the counter drives VgsLog.Throttled, which must survive the session and reset on
// unload — a per-subsystem counter would reset whenever a subsystem was rebuilt.
Frame++;
```

Non-obvious constraints, verified API behaviour and rejected alternatives belong in a
comment next to the code they constrain.

## 6. Before you report done

1. `verify-compile.ps1` prints `COMPILE OK`.
2. `review-check.ps1` prints `REVIEW OK — no findings`.
3. `offline-check.ps1` prints `OFFLINE OK`, and any new graph/geometry rule has an assertion
   in `tests/OfflineChecks.cs`.
4. No new `using` in `Vgs.Model` that references the game.
5. Every new public type has an XML doc comment explaining its role.
6. No unguarded game call at a boundary.
7. If behaviour is user-visible, hand the reviewer a list of what to click and what to expect.
8. If the change contradicts an ADR, **stop and raise it**. Do not implement around the
   decision, and do not edit the ADR yourself — that is the architect's call, and the
   contradiction goes through the joint review in `docs/review/joint-review.md`.

## 7. Working with the reviewer

The reviewer judges two things: whether the code does what the architecture said, and whether
it is good C#. Expect findings in both categories.

- Fix blockers; they mean the change cannot merge.
- For a major finding you disagree with, answer with a fact (a measurement, a dump, a log
  line), not an opinion. If the disagreement is really about the decision rather than the
  code, it escalates to a joint review — that is a normal outcome, not a failure.
- `// review:allow R0NN <reason>` is for a rule that genuinely does not apply on that line.
  Write the reason so the next reader does not have to re-derive it. Suppressing to make the
  gate quiet is a defect.

