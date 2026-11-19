# Block catalog — research notes

Build-time snapshot of every block subtype shipped with the local Space Engineers install,
produced by [`tools/build-block-catalog.ps1`](../../tools/build-block-catalog.ps1) and consumed by the
node-graph editor mod (stage 1: add a button to cockpit blocks).

| Artifact | Path |
| --- | --- |
| Generator (re-runnable, read-only w.r.t. the game) | [`tools/build-block-catalog.ps1`](../../tools/build-block-catalog.ps1) |
| Generated catalog | [`data/block-catalog.json`](../../data/block-catalog.json) (1 559 entries, ~604 KB, indent 2) |
| Source data | `H:\SteamLibrary\steamapps\common\SpaceEngineers\Content\Data\CubeBlocks\*.sbc` |

Regenerate with:

```powershell
pwsh -File .\tools\build-block-catalog.ps1
# or against another install / output path
pwsh -File .\tools\build-block-catalog.ps1 -GameData 'D:\Steam\...\Content\Data' -Out '.\data\block-catalog.json'
```

The script is idempotent: a second run on unchanged game data prints
`[catalog] unchanged ...` and does not touch the file (no timestamps, no machine paths, ordinal sort).

---

## 1. What was found

| Metric | Value |
| --- | --- |
| `.sbc` files scanned | 49 |
| `<Definition>` elements parsed | 1 576 |
| Catalog entries written (unique `subtypeId`) | **1 559** |
| Distinct `typeId` values | 91 |
| Skipped — empty `SubtypeId` (legacy default definition) | 13 |
| Duplicate `subtypeId` (later definition won) | 4 definitions / 3 keys |
| Files that failed to parse | 0 |
| Definitions without `xsi:type` (typeId derived from `<Id><TypeId>`) | 961 |

Split by grid size: **Large 963 / Small 596** (1 559 total).

All 13 skipped definitions have `<Id><SubtypeId/>` empty: `Door`, `AirtightHangarDoor`,
`GravityGenerator`, `GravityGeneratorSphere`, `Passage`, `Ladder2`, `OxygenTank`, `AirVent`,
`OxygenGenerator`, `LargeGatlingTurret`, `LargeMissileTurret`, `SmallMissileLauncher`,
`SmallGatlingGun`. They are the hidden "default" definition of their type (`<GuiVisible>false</GuiVisible>`
in most cases) and are not addressable by subtype name, so they cannot live in a `subtypeId`-keyed map.

Duplicates (last file/definition wins, matching load semantics):

| subtypeId | occurrences | files |
| --- | --- | --- |
| `DebugSphereLarge` | 3 (`DebugSphere1`, `DebugSphere2`, `DebugSphere3`) | `CubeBlocks_Extras.sbc` |
| `LargePistonBase` | 2 | `CubeBlocks_Mechanical.sbc` |
| `SmallPistonBase` | 2 | `CubeBlocks_Mechanical.sbc` |

Because of the `DebugSphereLarge` collapse only `MyObjectBuilder_DebugSphere3` survives in the catalog —
harmless, those blocks are `Public=false` debug geometry.

## 2. Fields extracted

| Field | Source element | Notes |
| --- | --- | --- |
| `subtypeId` | `<Id><SubtypeId>` (fallback `<Id><SubtypeName>`) | primary key, dedupe + sort key |
| `typeId` | `xsi:type` of `<Definition>` with the `Definition` suffix stripped (`MyObjectBuilder_CockpitDefinition` → `MyObjectBuilder_Cockpit`) | fallback `MyObjectBuilder_` + `<Id><TypeId>` for the 961 untyped definitions; matches the runtime class name used in blueprints |
| `displayName` | `<DisplayName>` | **localization key**, e.g. `DisplayName_Block_Cockpit2` — see gaps |
| `cubeSize` | `<CubeSize>` | `Large` / `Small`, never empty |
| `blockSize` | `<Size x y z>` or `<Size><X><Y><Z>` | `"1x1x1"` form; 1 140 blocks are 1×1×1, 67 distinct sizes, largest `5x5x10` |
| `public` | `<Public>` | default `true`; 47 entries are `false` |
| `enabled` | `<Enabled>` | **absent everywhere** → always `true` |
| `pcOnly` | `<PCOnly>` | **absent everywhere** → always `false` |
| `mass` | `<Mass>` | **absent everywhere** → always `null` |
| `category` | `<BlockPairName>` → `<Group>` → `<Category>` | block *family* key, e.g. `CockpitLarge`, `PassengerSeat`; 42 entries `null` |
| `hasTerminalControls` | heuristic, see §4 | 666 `true` / 893 `false` |
| `isCockpitLike` | `typeId` matches `*Cockpit*` | 50 `true` |

Validation: the example blueprint `html/bp-gr.sbc` was parsed as a control sample. It uses 10 distinct
subtypes; **all 10 are present in the catalog and all 10 typeIds match exactly** (`SubtypeName` in the
blueprint == `subtypeId` in the catalog, `xsi:type` == `typeId`), which confirms the `xsi:type` →
runtime-type normalization and that blueprint data can be joined against the catalog directly.

## 3. Cockpit-like blocks (stage-1 target)

**50 blocks** have a `typeId` containing `Cockpit` — and all 50 are `MyObjectBuilder_Cockpit`
(29 Large / 21 Small). There is no second cockpit class in the data, so `isCockpitLike` is currently
equivalent to `typeId == "MyObjectBuilder_Cockpit"`.

Only 23 of them have "Cockpit" in the subtype name; the other 27 are seats, chairs and pure decoration
that reuse the cockpit class because that is how SE implements "player can sit here".
`LargeBlockCockpitSeat` matches the name pattern but is grouped with the seats below, so the tables read
22 + 28.

### True cockpits (22)

| subtypeId | cubeSize |
| --- | --- |
| `BuggyCockpit` | Small |
| `CockpitOpen` | Large |
| `DBSmallBlockFighterCockpit` | Small |
| `FighterCockpitType2` | Small |
| `LargeBlockClosedSlopedCockpit` | Large |
| `LargeBlockCockpit` | Large |
| `LargeBlockCockpitIndustrial` | Large |
| `LargeBlockModularBridgeCockpit` | Large |
| `LargeBlockOpenSlopedCockpit` | Large |
| `LargeBlockStandingCockpit` | Large |
| `OpenCockpitLarge` | Large |
| `OpenCockpitSmall` | Small |
| `RoverCockpit` | Small |
| `SmallBlockCapCockpit` | Small |
| `SmallBlockClosedSlopedCockpit` | Small |
| `SmallBlockCockpit` | Small |
| `SmallBlockCockpitIndustrial` | Small |
| `SmallBlockFlushCockpit` | Small |
| `SmallBlockOpenSlopedCockpit` | Small |
| `SmallBlockStandingCockpit` | Small |
| `SpeederCockpit` | Small |
| `SpeederCockpitCompact` | Small |

(22 rows; `LargeBlockCockpitSeat` is in the seat table below because of its name.)

### Seats / decor that are cockpit-class but not named "Cockpit" (28)

| subtypeId | cubeSize | category |
| --- | --- | --- |
| `LargeBlockBathroom` | Large | Bathroom |
| `LargeBlockBathroomOpen` | Large | BathroomOpen |
| `LargeBlockCaptainChair` | Large | CaptainChair |
| `LargeBlockCaptainDesk` | Large | CaptainDesk |
| `LargeBlockCockpitSeat` | Large | Cockpit |
| `LargeBlockConsoleModuleInvertedCorner` | Large | ConsoleModuleInvertedCorner |
| `LargeBlockConsoleModuleScreens` | Large | ConsoleModuleScreens |
| `LargeBlockCouch` | Large | Couch |
| `LargeBlockCouchCorner` | Large | CouchCorner |
| `LargeBlockDesk` | Large | Desk |
| `LargeBlockDeskCorner` | Large | DeskCorner |
| `LargeBlockDeskCornerInv` | Large | DeskCornerInv |
| `LargeBlockInsetPlantCouch` | Large | Inset_PlantCouch |
| `LargeBlockJumpSeat` | Large | JumpSeat |
| `LargeBlockJumpSeatL` | Large | JumpSeatL |
| `LargeBlockJumpSeatR` | Large | JumpSeatR |
| `LargeBlockLabDeskSeat` | Large | LabDeskSeat |
| `LargeBlockSuspendedControlSeat` | Large | SuspendedControlSeat |
| `LargeBlockSuspendedControlSeatB` | Large | SuspendedControlSeatB |
| `LargeBlockToilet` | Large | Toilet |
| `PassengerBench` | Small | PassengerBench |
| `PassengerSeatLarge` | Large | PassengerSeat |
| `PassengerSeatSmall` | Small | PassengerSeatOld |
| `PassengerSeatSmallNew` | Small | PassengerSeat |
| `PassengerSeatSmallOffset` | Small | OffsetPassengerSeat |
| `SmallBlockJumpSeat` | Small | JumpSeat |
| `SmallBlockSuspendedControlSeat` | Small | SuspendedControlSeat |
| `SmallBlockSuspendedControlSeatB` | Small | SuspendedControlSeatB |

Two consequences for the mod:

* A UI that keys off the *subtype name* would miss 27 blocks and would also put a toilet and a couch in
  the "cockpit" bucket — use `isCockpitLike` / `typeId`.
* `PassengerSeatSmall` is `public = false` (superseded by `PassengerSeatSmallNew`); it is the only
  cockpit-class block that is not public, so it can be filtered out of build menus.

### Seat-like blocks that are NOT `MyObjectBuilder_Cockpit`

* `MyObjectBuilder_CryoChamber` — 10 blocks, all passenger-carrying furniture:
  `LargeBlockBed`, `LargeBlockBedFree`, `LargeBlockCryoChamber`, `LargeBlockCryoLabVat`,
  `LargeBlockCryoRoom`, `LargeBlockHalfBed`, `LargeBlockHalfBedOffset`, `LargeBlockInsetBed`,
  `SmallBlockBunkBed`, `SmallBlockCryoChamber`.
* `MyObjectBuilder_RemoteControl` — 2 blocks (`LargeBlockRemoteControl`, `SmallBlockRemoteControl`):
  control blocks, not seats; a player cannot sit in them.
* Adjacent controller families (not seats, but "control" in the gameplay sense):
  `MyObjectBuilder_FlightMovementBlock` (2), `MyObjectBuilder_DefensiveCombatBlock` (2),
  `MyObjectBuilder_OffensiveCombatBlock` (2), `MyObjectBuilder_TurretControlBlock` (2),
  `MyObjectBuilder_EventControllerBlock` (2).

If stage 1 should also cover "any block a player can control a grid from", the set is
50 cockpit-class + 10 cryo + 2 remote control = 62, not 50.

## 4. Terminal controls: what is likely to have them

The SBC schema does not declare terminal controls — that is a property of the C# class
(`MyObjectBuilder_TerminalBlock` and friends). The script therefore uses an explicit, editable
**deny list** (`$NoTerminalTypes` in the script) and treats everything else as terminal-capable:

| Runtime type | Count | Why no terminal |
| --- | --- | --- |
| `MyObjectBuilder_CubeBlock` | 824 | armor, windows, catwalks, corridors, pipes, signage, decorative hull |
| `MyObjectBuilder_ConveyorConnector` | 22 | conveyor tubes / pipe segments |
| `MyObjectBuilder_EmissiveBlock` | 21 | neon tubes |
| `MyObjectBuilder_Conveyor` | 12 | conveyor blocks, junctions, conveyor converters |
| `MyObjectBuilder_ExhaustBlock` | 4 | exhaust pipes and caps |
| `MyObjectBuilder_PistonTop` | 4 | piston head half |
| `MyObjectBuilder_Ladder2` | 3 | ladder segments |
| `MyObjectBuilder_SpaceBall` | 2 | space ball toy |
| `MyObjectBuilder_DebugSphere3` | 1 | debug geometry |
| **no terminal (total)** | **893** | |
| **terminal (total)** | **666** | doors, lights, LCDs, cargo, thrusters, cockpits, reactors, batteries, gyros, turrets, production, conveyor sorters, timers, sensors, antennas, … |

So roughly **43 % of subtypes expose a terminal panel**. Note that `hasTerminalControls` is a
*capability* guess, not a guarantee: `LargeBlockToilet` is cockpit-class and gets `true`, while a few
borderline families (`EmissiveBlock` neon tubes, `SpaceBall`, `ExhaustBlock`) are listed as
no-terminal and are the first candidates to re-check if the mod's UI ever looks wrong. Adjust the
`$NoTerminalTypes` array and re-run — the heuristic is deliberately one editable list, not scattered
if-statements.

## 5. Known gaps and limitations

1. **`mass` is always `null`.** No `<Mass>` element exists anywhere in `CubeBlocks\*.sbc`; block mass is
   computed at runtime from `<Components>` (component mass × count). Deriving it would mean parsing
   `PhysicalItems.sbc` for component masses — not done here. If the node graph needs mass, add a second
   pass over `PhysicalItems.sbc` or read `MyCubeBlockDefinition.Mass` at runtime.
2. **`enabled` / `pcOnly` are always the defaults** (`true` / `false`) — neither element occurs in the
   shipped data. They are kept in the schema so that modded SBCs that do use them round-trip correctly.
3. **`displayName` is a localization key, not a human string.** e.g. `LargeBlockCockpit` →
   `DisplayName_Block_Cockpit2`. Resolving it requires the `.resx`/localization files in
   `Content\Data\Localization`; 4 entries (`LargeRailStraight`, `LargeRoundArmor_CornerInv`, `Monolith`,
   `Stereolith`) have no `<DisplayName>` at all and fall back to the subtypeId.
4. **`category` is a block-family key** (`<BlockPairName>`), not the build-menu category. The
   build-menu grouping lives in `BlockCategories.sbc` (`<ItemIds><string>CubeBlock/Subtype</string>`),
   which was not merged in because one block can appear in several categories. 42 entries have no
   `BlockPairName`/`Group`/`Category` and are `null`.
5. **DLC ownership is not modelled.** `<DLC>` tags (e.g. `SparksOfTheFuture`, `Automatons`) exist per
   definition; a block the player does not own cannot be placed. Not extracted — easy to add.
6. **No mod blocks, no scenario blocks.** Only the vanilla `Content\Data` folder is scanned. Blocks
   added by other mods (or by this mod) are invisible to the catalog.
7. **Load order is filename order.** Overrides are resolved by sorting `*.sbc` by filename, which is
   deterministic but not guaranteed to equal the game's own load order for conflicting definitions
   (only 4 definitions actually collide today).
8. **`xsi:type` vs `<Id><TypeId>` disagree twice**: `LargeBlockBarCounter` and
   `LargeBlockBarCounterCorner` declare `<Id><TypeId>CubeBlock</TypeId>` but
   `xsi:type="MyObjectBuilder_PlanterDefinition"`. The script prefers `xsi:type` (→
   `MyObjectBuilder_Planter`), which matches what the game instantiates.
9. **Grid-size semantics**: `cubeSize` is the block's own grid size (`Large`/`Small`), not the size of
   the grid it sits on. `blockSize` is in grid cells, so `3x3x3` means different world sizes on large
   vs small grids.
10. **`Public=false` blocks are kept** (47 of them: 32 wheels, 13 `CubeBlock`, 1 cockpit, 1 debug sphere).
    They are hidden/legacy build-menu entries; filter with `public` if the UI should only offer
    placeable blocks.

## 6. Example entries

```json
{
  "subtypeId": "LargeBlockCockpit",
  "typeId": "MyObjectBuilder_Cockpit",
  "displayName": "DisplayName_Block_Cockpit2",
  "cubeSize": "Large",
  "blockSize": "1x1x1",
  "public": true,
  "enabled": true,
  "pcOnly": false,
  "mass": null,
  "category": "CockpitLarge",
  "hasTerminalControls": true,
  "isCockpitLike": true
},
{
  "subtypeId": "PassengerSeatLarge",
  "typeId": "MyObjectBuilder_Cockpit",
  "displayName": "DisplayName_Block_PassengerSeat",
  "cubeSize": "Large",
  "blockSize": "1x1x1",
  "public": true,
  "enabled": true,
  "pcOnly": false,
  "mass": null,
  "category": "PassengerSeat",
  "hasTerminalControls": true,
  "isCockpitLike": true
},
{
  "subtypeId": "FighterCockpitType2",
  "typeId": "MyObjectBuilder_Cockpit",
  "displayName": "DisplayName_Block_FighterCockpitType2",
  "cubeSize": "Small",
  "blockSize": "3x3x5",
  "public": true,
  "enabled": true,
  "pcOnly": false,
  "mass": null,
  "category": "FighterCockpitType2",
  "hasTerminalControls": true,
  "isCockpitLike": true
},
{
  "subtypeId": "LargeBlockCryoChamber",
  "typeId": "MyObjectBuilder_CryoChamber",
  "displayName": "DisplayName_Block_CryoChamber",
  "cubeSize": "Large",
  "blockSize": "1x1x1",
  "public": true,
  "enabled": true,
  "pcOnly": false,
  "mass": null,
  "category": "CryoChamber",
  "hasTerminalControls": true,
  "isCockpitLike": false
},
{
  "subtypeId": "LargeBlockArmorBlock",
  "typeId": "MyObjectBuilder_CubeBlock",
  "displayName": "DisplayName_Block_LightArmorBlock",
  "cubeSize": "Large",
  "blockSize": "1x1x1",
  "public": true,
  "enabled": true,
  "pcOnly": false,
  "mass": null,
  "category": "LightArmorBlock",
  "hasTerminalControls": false,
  "isCockpitLike": false
},
{
  "subtypeId": "LargeBlockConveyor",
  "typeId": "MyObjectBuilder_Conveyor",
  "displayName": "DisplayName_Block_Conveyor",
  "cubeSize": "Large",
  "blockSize": "1x1x1",
  "public": true,
  "enabled": true,
  "pcOnly": false,
  "mass": null,
  "category": "Conveyor",
  "hasTerminalControls": false,
  "isCockpitLike": false
},
{
  "subtypeId": "LargeProjector",
  "typeId": "MyObjectBuilder_Projector",
  "displayName": "DisplayName_Block_Projector",
  "cubeSize": "Large",
  "blockSize": "1x1x1",
  "public": true,
  "enabled": true,
  "pcOnly": false,
  "mass": null,
  "category": "Projector",
  "hasTerminalControls": true,
  "isCockpitLike": false
}
```

## 7. Runtime enumeration vs build-time snapshot

A mod *could* enumerate blocks at runtime instead of shipping this file:
`MyDefinitionManager.Static.GetAllDefinitions<MyCubeBlockDefinition>()` (or
`GetDefinition<MyCubeBlockDefinition>(new MyDefinitionId(typeof(MyObjectBuilder_Cockpit), subtype))`)
returns the live definition objects, from which `Id.SubtypeId`, `Id.TypeId`, `CubeSize`, `Size`,
`Public`, `DisplayName` and `Mass` can be read directly — and that is the only way to see blocks added by
other mods, including mods loaded after this one. But runtime enumeration only exists inside a running
session: it cannot be used by build tooling, by the node-graph editor's design-time validation, by
tests, or to diff two game versions, and it forces every UI that needs "all cockpits" to run in-game
after the definition manager is initialized. The catalog is therefore a **build-time snapshot** of
vanilla data: cheap to load (`JsonConvert.DeserializeObject<List<BlockInfo>>`), diffable in git,
usable from a static analyzer — at the cost of going stale whenever the game or the mod list changes,
which is why regeneration is a one-command script and the file is regenerated per game update rather
than hand-edited. The pragmatic split is: snapshot for design-time/UI defaults, live
`MyDefinitionManager` lookup at runtime for anything that must be authoritative (especially cockpit
detection on modded blocks, which should fall back to `block is MyCockpit` / `MyDefinitionId` checks
rather than the catalog's `isCockpitLike` flag).
