using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using VRageMath;
using Vgs.Core;
using Vgs.Model;
using Vgs.Ui;

// Offline checks for the parts of the editor that are pure data and pure geometry: the graph
// invariants, the toolbar placement rules, the port-slot layout, the card and port rectangles,
// and the wire router.
//
// Why this file exists: the in-game feedback loop costs a full game launch, and these are
// exactly the things that decide whether the picture matches what the player did. If the slot
// layout is wrong the bars overlap; if the geometry is wrong the dots and the hit tests
// disagree; if the router is wrong the lines land somewhere else; if the toolbar rules are
// wrong a connection is written into a cell the game will not fire or will silently clear.
// None of that needs Space Engineers, and `Vgs.Model` plus `Vgs.Ui.PortGeometry`/`EdgeRouting`
// deliberately have no game references so this can run without it.
//
// Run it with `pwsh -File tools/offline-check.ps1`. It compiles this file together with the
// model and geometry sources against the game's math assemblies only, then executes it.
//
// It is not a unit-test framework: no discovery, no attributes, no mocking. A check is a
// comparison and a line of text naming what it asserted, so a failure reads as a sentence.
internal static class OfflineChecks
{
    private static int _failures;
    private static int _checks;

    private static void Check(bool condition, string what)
    {
        _checks++;
        if (condition) return;
        _failures++;
        Console.WriteLine("FAIL: " + what);
    }

    private static void CheckClose(float actual, float expected, string what)
    {
        _checks++;
        if (Math.Abs(actual - expected) < 1e-5f) return;
        _failures++;
        Console.WriteLine("FAIL: " + what + " (expected " + expected + ", got " + actual + ")");
    }

    /// <summary>
    /// A timer-shaped node: one fire group, nine slots, ten pages. That is what the game
    /// builds for <c>MyTimerBlock</c> (<c>new MyToolbar(ButtonPanel, 9, 10)</c>).
    /// </summary>
    private static ToolbarLayout TimerToolbar()
    {
        return new ToolbarLayout(0, 9, 10, false);
    }

    /// <summary>
    /// A sensor-shaped node: two slots, ten pages, yes/no. That is what the game builds for
    /// <c>MySensorBlock</c> (<c>new MyToolbar(ButtonPanel, 2, 10)</c> plus
    /// <c>CanShowYesNoBackgrounds</c> left at its default).
    /// </summary>
    private static ToolbarLayout SensorToolbar()
    {
        return new ToolbarLayout(0, 2, 10, true);
    }

    private static Node MakeNode(long id, string subtype, params string[] actions)
    {
        return MakeNode(id, subtype, TimerToolbar(), actions);
    }

    /// <summary>
    /// A free cell, as the writer reports an empty toolbar slot. Used as the <c>cellAt</c>
    /// answer in the placement checks below.
    /// </summary>
    private static CellContents Free()
    {
        return CellContents.Empty;
    }

    /// <summary>
    /// A cell holding an item the placement rule must not touch: a block action on some other
    /// block. The identity is irrelevant to the checks — what matters is that the value is not
    /// <see cref="CellContents.Empty"/>.
    /// </summary>
    private static CellContents Taken()
    {
        return CellContents.BlockAction(424242L, "OnOff");
    }

    private static Node MakeNode(long id, string subtype, ToolbarLayout toolbar, params string[] actions)
    {
        var node = new Node(id, subtype, "block" + id, Vector3I.Zero, toolbar);
        var list = new List<string>(actions);
        BlockPortCatalog.FillActions(node, list);
        BlockPortCatalog.FillEvents(node);
        node.HasTerminal = list.Count > 0;
        return node;
    }

    /// <summary>A block that cannot drive anything: no toolbar at all.</summary>
    private static Node MakeTarget(long id, string subtype, params string[] actions)
    {
        var node = new Node(id, subtype, "block" + id, Vector3I.Zero, ToolbarLayout.None);
        var list = new List<string>(actions);
        BlockPortCatalog.FillActions(node, list);
        BlockPortCatalog.FillEvents(node);
        node.HasTerminal = list.Count > 0;
        return node;
    }

    public static int Main()
    {
        Console.WriteLine("=== toolbar layout ===");
        ToolbarLayouts();

        Console.WriteLine("=== toolbar placement (single group) ===");
        PlacementSingleGroup();

        Console.WriteLine("=== toolbar placement (yes/no) ===");
        PlacementYesNo();

        Console.WriteLine("=== cell contents and placement (R-009) ===");
        CellContentsAndPlacement();

        Console.WriteLine("=== which blocks can drive others ===");
        Drivability();

        Console.WriteLine("=== port slots and fan-in ===");
        SlotsAndFanIn();

        Console.WriteLine("=== graph invariants ===");
        Invariants();

        Console.WriteLine("=== a block driving itself (ADR-0036) ===");
        SelfConnection();

        Console.WriteLine("=== card and port geometry ===");
        Geometry();

        Console.WriteLine("=== card button row (ADR-0020) ===");
        CardButtonRow();

        Console.WriteLine("=== port controls: dot, remove button, label ===");
        PortControls();

        Console.WriteLine("=== removing a port's connections ===");
        PortRemoval();

        Console.WriteLine("=== wire routing ===");
        Routing();

        Console.WriteLine("=== node removal ===");
        Removal();

        Console.WriteLine("=== slot contiguity (input placement depends on it) ===");
        SlotContiguity();

        Console.WriteLine("=== persisted position format (ADR-0012) ===");
        PersistedPositionFormat();

        Console.WriteLine("=== placed flag (ADR-0012) ===");
        PlacementFlag();

        Console.WriteLine("=== automatic layout is deterministic (ADR-0012) ===");
        LayoutIsDeterministic();

        Console.WriteLine("=== construct layout keeps grids together (ADR-0032) ===");
        ConstructLayoutKeepsGridsTogether();

        Console.WriteLine("=== grid frames enclose their own cards (ADR-0032) ===");
        GridFramesEncloseTheirGrid();

        Console.WriteLine("=== canvas culling and clipped borders (R-015) ===");
        CanvasCullingAndClippedBorders();

        Console.WriteLine("=== block group action rule (ADR-0015) ===");
        GroupActionRule();

        Console.WriteLine("=== action captions (ADR-0037) ===");
        ActionCaptions();

        Console.WriteLine("=== action ids name their controls (ADR-0039) ===");
        ActionControlMapping();

        Console.WriteLine("=== block group nodes (ADR-0015) ===");
        GroupNodes();

        Console.WriteLine("=== connections to a block group (ADR-0015) ===");
        GroupEdges();

        Console.WriteLine("=== block group frame geometry (ADR-0015) ===");
        GroupGeometry();

        Console.WriteLine("=== dragging a group by its title strip (ADR-0016) ===");
        GroupDrag();

        Console.WriteLine("=== watch anchor and watch ports (ADR-0017) ===");
        WatchAnchorAndPorts();

        Console.WriteLine("=== dashed watch line (ADR-0017) ===");
        WatchDashes();

        Console.WriteLine("=== watch list format (ADR-0017) ===");
        WatchListFormat();

        Console.WriteLine("=== watch list reconciled with the game (ADR-0018) ===");
        WatchReconciliation();

        Console.WriteLine("=== icon atlas table (ADR-0019) ===");
        IconAtlasTable();

        Console.WriteLine("=== icon placement and clipping (ADR-0019) ===");
        IconPlacementChecks();

        Console.WriteLine("=== view state format (ADR-0022) ===");
        ViewStateFormat();

        Console.WriteLine("=== block search rule (ADR-0023) ===");
        BlockSearchRule();

        Console.WriteLine("=== block state snapshot (Step 2) ===");
        BlockStateSnapshot();

        Console.WriteLine("=== card power switch (ADR-0035) ===");
        PowerSwitch();

        Console.WriteLine("=== block category and descriptor (ADR-0030) ===");
        BlockCategories();

        Console.WriteLine("=== controller state is the flag (ADR-0030) ===");
        ControllerSpecificity();

        Console.WriteLine("=== text field editing (ADR-0024) ===");
        TextFieldEditing();

        Console.WriteLine("=== search field layout (Step 4.2) ===");
        SearchFieldLayout();

        Console.WriteLine("=== view state writes (Step 4.3, ADR-0022) ===");
        ViewStateWrites();

        Console.WriteLine("=== conveyor network reduction (ADR-0040) ===");
        ConveyorNetworkReduction();

        Console.WriteLine("=== conveyor pipe rails (ADR-0040) ===");
        ConveyorPipeRails();

        Console.WriteLine("=== localization tables (ADR-0033) ===");
        Localization();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "OFFLINE OK — " + _checks + " checks"
            : "OFFLINE FAILED — " + _failures + " of " + _checks + " checks");

        return _failures == 0 ? 0 : 1;
    }

    private static void ToolbarLayouts()
    {
        ToolbarLayout timer = TimerToolbar();
        Check(timer.CanDrive, "a 9x10 toolbar can drive");
        Check(timer.CellCount == 90, "a 9x10 toolbar has 90 cells");
        Check(!timer.YesNo, "a timer toolbar is a single-group toolbar");
        Check(timer.SlotFor(PortGroup.Single) == -1, "a single-group port may take any slot");

        ToolbarLayout sensor = SensorToolbar();
        Check(sensor.YesNo, "a 2x10 toolbar is a yes/no toolbar");
        Check(sensor.CellCount == 20, "a 2x10 toolbar has 20 cells");
        Check(sensor.SlotFor(PortGroup.Yes) == 0, "the yes group writes slot 0");
        Check(sensor.SlotFor(PortGroup.No) == 1, "the no group writes slot 1");

        // The game's linear index is page * SlotCount + slot; the checks below depend on it
        // matching, because a cell read back from the toolbar is addressed that way.
        Check(sensor.CellIndex(0, 0) == 0, "page 0 slot 0 is cell 0");
        Check(sensor.CellIndex(0, 1) == 1, "page 0 slot 1 is cell 1");
        Check(sensor.CellIndex(3, 1) == 7, "page 3 slot 1 is cell 7");
        Check(sensor.PageOf(7) == 3, "cell 7 is on page 3");
        Check(sensor.SlotOf(7) == 1, "cell 7 is in slot 1");
        Check(sensor.SlotOf(6) == 0, "cell 6 is in slot 0");

        Check(ToolbarLayout.None.CanDrive == false, "the empty layout cannot drive");
        Check(ToolbarLayout.None.CellCount == 0, "the empty layout has no cells");
        Check(ToolbarLayout.None.SlotFor(PortGroup.Yes) == 0,
              "the empty layout still answers the slot question without throwing");
    }

    /// <summary>
    /// A single-group toolbar: any free cell, and the same action may appear once per page.
    /// This is the timer case, and it is where "the tenth repeating action" comes from.
    ///
    /// The third argument is the cell contents, not a verdict — the planner decides what
    /// "free" means. That is the shape R-009 asks for: when the caller passed a pre-computed
    /// bool, an inverted argument made every empty toolbar report "no free cell".
    /// </summary>
    private static void PlacementSingleGroup()
    {
        ToolbarLayout layout = TimerToolbar();

        // Empty toolbar: the first cell of the first page.
        int index;
        Check(ToolbarPlanner.Plan(layout, PortGroup.Single, c => Free(), p => false, out index)
              == ToolbarPlacement.Ok, "an empty toolbar accepts a connection");
        Check(index == 0, "the first connection takes cell 0");

        // Page 0 full, page 1 empty: the next free page is used, not a refusal.
        Check(ToolbarPlanner.Plan(layout, PortGroup.Single,
                                  c => c >= 9 ? Free() : Taken(), p => false, out index)
              == ToolbarPlacement.Ok,
              "a full first page pushes the connection to the next page");
        Check(index == 9, "the connection lands on cell 9, i.e. page 1 slot 0");

        // The duplicate rule: page 0 already holds this action, so page 1 is used even though
        // page 0 has free cells. The vanilla screen would delete the duplicate.
        Check(ToolbarPlanner.Plan(layout, PortGroup.Single, c => Free(), p => p == 0, out index)
              == ToolbarPlacement.Ok, "a duplicate on page 0 still finds a cell");
        Check(index == 9, "the repeat is written to the next page, not next to the first copy");

        // Every page holds it: impossible, and the caller has to say so.
        Check(ToolbarPlanner.Plan(layout, PortGroup.Single, c => Free(), p => true, out index)
              == ToolbarPlacement.ActionOnEveryPage,
              "the same action on every page is refused as a duplicate, not as a full toolbar");
        Check(index == -1, "a refused placement reports no cell");

        // Every page is full with other actions: a different failure, and a different message.
        Check(ToolbarPlanner.Plan(layout, PortGroup.Single, c => Taken(), p => false, out index)
              == ToolbarPlacement.NoFreeCell, "a completely full toolbar is reported as full");

        // Ten pages is exactly the limit for ten identical connections: the eleventh is the
        // case the roadmap calls out, and it must be a refusal rather than a silent overwrite.
        var taken = new bool[layout.CellCount];
        int placed = 0;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            ToolbarPlacement p = ToolbarPlanner.Plan(
                layout, PortGroup.Single,
                c => taken[c] ? Taken() : Free(),
                page => PageHolds(taken, layout, page),
                out index);
            if (p != ToolbarPlacement.Ok) break;
            taken[index] = true;
            placed++;
        }
        Check(placed == 10, "ten identical connections fit a ten-page timer (got " + placed + ")");

        Check(ToolbarPlanner.Plan(layout, PortGroup.Single,
                                  c => taken[c] ? Taken() : Free(), page => PageHolds(taken, layout, page),
                                  out index) == ToolbarPlacement.ActionOnEveryPage,
              "the eleventh identical connection is refused");
    }

    /// <summary>True when the page already holds the action we are trying to place.</summary>
    private static bool PageHolds(bool[] taken, ToolbarLayout layout, int page)
    {
        // In this fixture every placed cell is "the same action", so a page holds it as soon
        // as one of its cells is taken.
        for (int s = 0; s < layout.SlotCount; s++)
        {
            if (taken[layout.CellIndex(page, s)]) return true;
        }
        return false;
    }

    /// <summary>
    /// A yes/no toolbar: the two groups write different slots, and they never mix. This is the
    /// sensor and event-controller case.
    /// </summary>
    private static void PlacementYesNo()
    {
        ToolbarLayout layout = SensorToolbar();

        int index;
        Check(ToolbarPlanner.Plan(layout, PortGroup.Yes, c => Free(), p => false, out index)
              == ToolbarPlacement.Ok, "a yes connection fits an empty yes/no toolbar");
        Check(index == 0, "the yes group writes cell 0 (page 0 slot 0)");

        Check(ToolbarPlanner.Plan(layout, PortGroup.No, c => Free(), p => false, out index)
              == ToolbarPlacement.Ok, "a no connection fits an empty yes/no toolbar");
        Check(index == 1, "the no group writes cell 1 (page 0 slot 1)");

        // A "no" connection must never take slot 0, even when slot 0 is the only free cell:
        // the game fires slot 0 on "yes", so the item would fire on the wrong event.
        Check(ToolbarPlanner.Plan(layout, PortGroup.No, c => c % 2 == 0 ? Free() : Taken(), p => false, out index)
              == ToolbarPlacement.NoFreeCell,
              "a no connection never falls back to a free yes slot");

        // With page 0's no slot taken by something else, the connection goes to page 1's no
        // slot — not to page 0's free yes slot, and not to page 1's yes slot.
        Check(ToolbarPlanner.Plan(layout, PortGroup.No, c => c >= 2 ? Free() : Taken(), p => false, out index)
              == ToolbarPlacement.Ok, "a no connection skips a page whose no slot is taken");
        Check(index == 3, "the no connection goes to page 1 slot 1, not to a free yes slot");

        // Both groups exhausted: the yes group has its own message and its own cell space.
        Check(ToolbarPlanner.Plan(layout, PortGroup.Yes, c => c % 2 == 1 ? Free() : Taken(), p => false, out index)
              == ToolbarPlacement.NoFreeCell, "a yes connection with only no slots free is refused");

        // The group of a cell read back from the toolbar: slot 0 is yes, slot 1 is no.
        Check(ToolbarPlanner.GroupOfCell(layout, 0) == PortGroup.Yes, "cell 0 is a yes cell");
        Check(ToolbarPlanner.GroupOfCell(layout, 1) == PortGroup.No, "cell 1 is a no cell");
        Check(ToolbarPlanner.GroupOfCell(layout, 8) == PortGroup.Yes, "cell 8 is a yes cell");
        Check(ToolbarPlanner.GroupOfCell(layout, 9) == PortGroup.No, "cell 9 is a no cell");
        Check(ToolbarPlanner.GroupOfCell(TimerToolbar(), 5) == PortGroup.Single,
              "every cell of a single-group toolbar is a single-group cell");
    }

    /// <summary>
    /// What a cell contains, and what the placement rule does with each case.
    ///
    /// This group exists because of R-009: the game layer used to hand the planner a
    /// pre-computed "is this cell free" bool, that bool was passed inverted, and every empty
    /// toolbar answered "no free cell" — so no connection could be created at all, from any
    /// block, while the planner's own checks stayed green. The planner now receives the cell
    /// contents and decides for itself, which makes the decision testable here and leaves the
    /// game layer nothing to invert.
    /// </summary>
    private static void CellContentsAndPlacement()
    {
        // The four cases, and the only one that may be written into.
        Check(CellContents.Empty.IsEmpty, "an empty cell reports itself empty");
        Check(!CellContents.BlockAction(7L, "OnOff").IsEmpty, "a block action is not an empty cell");
        Check(!CellContents.GroupAction("Lamps", "OnOff").IsEmpty, "a group action is not an empty cell");
        Check(!CellContents.Other("Definition").IsEmpty,
              "an item the editor cannot draw is NOT a free cell — it is somebody's wiring");

        // Identity: a block and a group with the same action id are different targets, so a
        // cell holding one must not satisfy a check for the other.
        CellContents blockCell = CellContents.BlockAction(7L, "OnOff");
        Check(blockCell.HoldsBlockAction(7L, "OnOff"), "a cell holds the block action it names");
        Check(!blockCell.HoldsBlockAction(8L, "OnOff"), "a different block is a different cell");
        Check(!blockCell.HoldsGroupAction("Lamps", "OnOff"), "a block item is not a group item");

        CellContents groupCell = CellContents.GroupAction("Lamps", "OnOff");
        Check(groupCell.HoldsGroupAction("Lamps", "OnOff"), "a cell holds the group action it names");
        Check(!groupCell.HoldsGroupAction("Doors", "OnOff"), "a different group is a different cell");
        Check(!groupCell.HoldsBlockAction(7L, "OnOff"), "a group item is not a block item");

        // The R-009 case, stated as a check: an empty toolbar takes the connection on the very
        // first page. Before the fix this returned NoFreeCell for the same input.
        int index;
        ToolbarLayout timer = TimerToolbar();
        Check(ToolbarPlanner.Plan(timer, PortGroup.Single, c => CellContents.Empty, p => false, out index)
              == ToolbarPlacement.Ok, "an empty single-group toolbar is not reported as full");
        Check(index == 0, "an empty single-group toolbar takes cell 0");

        ToolbarLayout sensor = SensorToolbar();
        Check(ToolbarPlanner.Plan(sensor, PortGroup.Yes, c => CellContents.Empty, p => false, out index)
              == ToolbarPlacement.Ok, "an empty yes/no toolbar is not reported as full");
        Check(index == 0, "an empty yes/no toolbar takes the yes cell of page 0");

        // An item the editor cannot draw occupies its cell but does not make the page a
        // duplicate: the connection goes to the next cell of the same page, not to page 1.
        Check(ToolbarPlanner.Plan(timer, PortGroup.Single,
                                  c => c == 0 ? CellContents.Other("Definition") : CellContents.Empty,
                                  p => false, out index) == ToolbarPlacement.Ok,
              "a foreign item in cell 0 still leaves the rest of the page usable");
        Check(index == 1, "the connection takes cell 1, leaving the foreign item in cell 0 alone");

        // A page whose cells are all foreign items is full for this purpose: the next page is
        // used rather than overwriting one of them.
        Check(ToolbarPlanner.Plan(timer, PortGroup.Single,
                                  c => c < 9 ? CellContents.Other("Definition") : CellContents.Empty,
                                  p => false, out index) == ToolbarPlacement.Ok,
              "a page of foreign items pushes the connection to the next page");
        Check(index == 9, "the connection lands on cell 9 instead of overwriting an item");
    }

    /// <summary>
    /// "Can this block drive other blocks" is answered by the toolbar shape, not by a table of
    /// subtypes. These checks pin that: the same subtype with and without a toolbar differs,
    /// and a block with no toolbar has no ports and no button.
    /// </summary>
    private static void Drivability()
    {
        Node timer = MakeNode(1, "LargeBlockTimerBlock", "TriggerNow");
        Check(timer.CanDriveBlocks, "a block with a toolbar can drive others");
        Check(timer.Events.Count == 1, "a single-group block exposes one port per event");
        Check(timer.PrimaryEvent != null && timer.PrimaryEvent.Key == "Triggered",
              "a timer's primary event is its own Triggered");
        Check(timer.PrimaryEvent.Group == PortGroup.Single, "a timer's port belongs to no group");

        Node sensor = MakeNode(2, "LargeBlockSensor", SensorToolbar(), "OnOff");
        Check(sensor.CanDriveBlocks, "a sensor can drive others");
        Check(sensor.Events.Count == 2, "a yes/no block exposes two ports per event");
        Check(sensor.FindEvent("Detected#true") != null, "the yes port has the #true suffix");
        Check(sensor.FindEvent("Detected#false") != null, "the no port has the #false suffix");
        Check(sensor.FindEvent("Detected#true").Group == PortGroup.Yes, "the #true port is the yes group");
        Check(sensor.FindEvent("Detected#false").Group == PortGroup.No, "the #false port is the no group");
        Check(sensor.PrimaryEventFor(PortGroup.Yes).Key == "Detected#true",
              "the yes group resolves to the yes port");
        Check(sensor.PrimaryEventFor(PortGroup.No).Key == "Detected#false",
              "the no group resolves to the no port");
        Check(sensor.PrimaryEventFor(PortGroup.Single) == null,
              "a yes/no block has no single-group port");

        // A subtype with no curated entry still gets a port: a modded timer-like block must
        // be usable, and "Triggered" is the honest generic name for "the toolbar fired".
        Node modded = MakeNode(3, "MyModdedSequencer", "Run");
        Check(modded.CanDriveBlocks, "an unknown subtype with a toolbar can still drive");
        Check(modded.FindEvent(BlockPortCatalog.GenericEventKey) != null,
              "an unknown subtype falls back to the generic event");

        // No toolbar: not a source, no ports, no button — even though the subtype is one the
        // curated table knows.
        Node noToolbar = MakeTarget(4, "LargeBlockTimerBlock", "TriggerNow");
        Check(!noToolbar.CanDriveBlocks, "a block without a toolbar cannot drive others");
        Check(noToolbar.Events.Count == 0, "a block without a toolbar exposes no event ports");
        Check(noToolbar.PrimaryEvent == null, "a block without a toolbar has no primary event");

        // A block with no curated event but a toolbar is still a source: the toolbar is the
        // evidence, not the table.
        Node conveyor = MakeNode(5, "LargeBlockConveyorTube");
        Check(conveyor.CanDriveBlocks, "the toolbar, not the subtype, decides drivability");
    }

    private static void SlotsAndFanIn()
    {
        var graph = new GridGraph(1);

        Node timer = MakeNode(10, "LargeBlockTimerBlock", "Start", "Stop", "TriggerNow");
        Node reactor = MakeTarget(20, "LargeBlockSmallGenerator", "OnOff", "OnOff_On", "OnOff_Off");
        Node door = MakeTarget(30, "LargeBlockDoor", "Open", "Close", "OnOff");

        graph.AddNode(timer);
        graph.AddNode(reactor);
        graph.AddNode(door);

        // Nothing connected: no port is drawn at all.
        graph.LayoutPorts();
        Check(DrawnOutputs(timer) == 0 && DrawnInputs(reactor) == 0,
              "unconnected ports are not drawn");

        Check(graph.TryConnect(10, "Triggered", 20, "OnOff", new ToolbarCell(0, 0)),
              "timer -> reactor OnOff");
        Check(graph.TryConnect(10, "Triggered", 30, "Open", new ToolbarCell(0, 1)),
              "timer -> door Open (fan-out)");
        Check(graph.TryConnect(20, "Triggered", 30, "Close", new ToolbarCell(0, 0)) == false,
              "a block without a toolbar cannot be a source");
        graph.LayoutPorts();

        // One output bar per output port, however many edges leave it.
        Check(DrawnOutputs(timer) == 1, "timer draws exactly one output bar for two outgoing edges");
        Check(timer.FindEvent("Triggered").Slot == 0, "timer's Triggered sits in slot 0");

        // One input bar per input port, however many edges arrive at it.
        Check(DrawnInputs(reactor) == 1, "reactor draws exactly one input bar for one incoming edge");
        Check(reactor.FindAction("OnOff").Slot == 0, "reactor's OnOff sits in slot 0");
        Check(graph.CountEdgesToInput(20, "OnOff") == 1, "reactor OnOff reports one source");

        Check(door.FindAction("Open").Slot == 0, "door's Open sits in slot 0");

        // A second connection to a different action of the same block stacks the next bar.
        Check(graph.TryConnect(10, "Triggered", 30, "Close", new ToolbarCell(0, 2)),
              "timer -> door Close");
        graph.LayoutPorts();
        Check(door.FindAction("Open").Slot == 0 && door.FindAction("Close").Slot == 1,
              "two input ports on one block occupy slots 0 and 1");
        Check(DrawnInputs(door) == 2, "door draws two input bars");

        // Ports are not renumbered when a slot is taken: order is catalog order, then edges.
        Check(door.FindAction("OnOff").Slot < 0, "an unconnected action is not drawn");
    }

    private static void Invariants()
    {
        var graph = new GridGraph(2);
        Node a = MakeNode(1, "LargeBlockTimerBlock", "TriggerNow");
        Node b = MakeTarget(2, "LargeBlockSmallGenerator", "OnOff");
        graph.AddNode(a);
        graph.AddNode(b);

        Check(!graph.TryConnect(1, "Triggered", 2, "Nope", new ToolbarCell(0, 0)),
              "unknown action port is rejected");
        Check(!graph.TryConnect(1, "Nope", 2, "OnOff", new ToolbarCell(0, 0)),
              "unknown event port is rejected");
        Check(!graph.TryConnect(1, "Triggered", 99, "OnOff", new ToolbarCell(0, 0)),
              "unknown target node is rejected");
        Check(!graph.TryConnect(1, "TriggerNow", 2, "OnOff", new ToolbarCell(0, 0)),
              "an action cannot be used as a source (directions are not interchangeable)");
        Check(!graph.TryConnect(1, "Triggered", 2, "OnOff_On", new ToolbarCell(0, 0)),
              "an event cannot be used as a target");

        Check(graph.TryConnect(1, "Triggered", 2, "OnOff", new ToolbarCell(0, 0)),
              "valid connection accepted");
        Check(!graph.TryConnect(1, "Triggered", 2, "OnOff", new ToolbarCell(0, 0)),
              "the same cell cannot hold two connections");
        Check(graph.EdgeCount == 1, "rejected attempts changed nothing");
        Check(graph.HasEdge(1, "Triggered", 2, "OnOff"), "HasEdge sees the connection");

        // A cell outside the toolbar is not a cell.
        Check(!graph.TryConnect(1, "Triggered", 2, "OnOff", new ToolbarCell(0, 90)),
              "a cell past the end of the toolbar is rejected");
        Check(!graph.TryConnect(1, "Triggered", 2, "OnOff", new ToolbarCell(3, 0)),
              "a cell of a toolbar the block does not have is rejected");
        Check(!graph.TryConnect(1, "Triggered", 2, "OnOff", ToolbarCell.None),
              "an edge without a cell is rejected");

        // The same action on another page is a legal, separate connection: that is exactly
        // the rule the toolbar writer relies on, and the graph must not "deduplicate" it.
        Check(graph.TryConnect(1, "Triggered", 2, "OnOff", new ToolbarCell(0, 9)),
              "the same action on another page is a separate connection");
        Check(graph.EdgeCount == 2, "both connections kept");
        Check(graph.CountEdgesFromOutput(1, "Triggered") == 2, "both edges leave the same port");

        // …but they are one line between the same two dots, so only one of them is drawn.
        graph.LayoutPorts();
        Check(graph.Lines.Count == 1, "two connections of one port pair draw one line");
        Check(graph.Edges.Count == 2, "the second connection still exists in the graph");

        // A duplicate is now only a duplicate of the *cell*.
        Check(!graph.TryConnect(1, "Triggered", 2, "OnOff", new ToolbarCell(0, 9)),
              "the same cell is still refused");
    }

    /// <summary>
    /// A block may drive itself (ADR-0036). The edge is an ordinary toolbar item whose target is
    /// its own source, which the game executes like any other — so the graph must accept it, put
    /// its two ends on the same card in two independent stacks, and keep the cell rules.
    /// </summary>
    private static void SelfConnection()
    {
        var graph = new GridGraph(7);
        Node timer = MakeNode(1, "LargeBlockTimerBlock", "Start", "TriggerNow");
        timer.Position = new Vector2(0.30f, 0.20f);
        graph.AddNode(timer);

        Check(graph.TryConnect(1, "Triggered", 1, "TriggerNow", new ToolbarCell(0, 0)),
              "a block may drive itself");
        Check(graph.EdgeCount == 1, "the self-connection is in the graph");
        Check(graph.HasEdge(1, "Triggered", 1, "TriggerNow"), "HasEdge finds a self-connection");
        Check(graph.CountEdgesToInput(1, "TriggerNow") == 1,
              "a self-connection counts as one source of its own input");

        // Both ends are bars on the same card, taken from two independent stacks: the event on
        // the outgoing side, the action on the incoming one. One shared slot would draw the two
        // bars on top of each other.
        graph.LayoutPorts();
        Check(timer.FindEvent("Triggered").Slot == 0,
              "the self-connection's output gets the first output slot");
        Check(timer.FindAction("TriggerNow").Slot == 0,
              "the self-connection's input gets the first input slot");
        Check(DrawnOutputs(timer) == 1 && DrawnInputs(timer) == 1,
              "a self-connection draws one bar on each side of its card");
        Check(graph.Lines.Count == 1, "a self-connection is one line");

        // The ordinary cell rules still apply: the loop occupies a cell like any other edge, and
        // two edges cannot share it. A second action of the same block is a separate cell.
        Check(!graph.TryConnect(1, "Triggered", 1, "Start", new ToolbarCell(0, 0)),
              "a second self-connection into the same cell is refused");
        Check(graph.TryConnect(1, "Triggered", 1, "Start", new ToolbarCell(0, 1)),
              "a self-connection to another action of the block is a separate connection");
        graph.LayoutPorts();
        Check(DrawnInputs(timer) == 2, "the second self-connection stacks another input bar");

        // Nothing keeps a self-edge alive on its own: removing the node takes the loop with it.
        Check(graph.RemoveNode(1), "the self-driving node is removed");
        Check(graph.EdgeCount == 0, "removing the node removes its self-connections");
    }

    private static void Geometry()
    {
        var graph = new GridGraph(3);
        Node src = MakeNode(1, "LargeBlockTimerBlock", "Start");
        Node dst = MakeTarget(2, "LargeBlockSmallGenerator", "OnOff");
        src.Position = new Vector2(0.30f, 0.20f);
        dst.Position = new Vector2(0.10f, 0.50f);
        graph.AddNode(src);
        graph.AddNode(dst);

        Check(graph.TryConnect(1, "Triggered", 2, "OnOff", new ToolbarCell(0, 0)),
              "connect for geometry test");
        graph.LayoutPorts();

        // Card.
        Vector2 cardTopLeft, cardSize;
        PortGeometry.CardRect(src, out cardTopLeft, out cardSize);
        CheckClose(cardTopLeft.X, 0.30f, "card x is the node position");
        CheckClose(cardSize.X, VgsConstants.NodeWidth, "card width");
        CheckClose(cardSize.Y, VgsConstants.NodeHeight, "card height");

        // The output bar's top edge touches the card's bottom edge, and it is exactly as wide
        // as the card: a port belongs to the block, so it spans the block.
        Port outPort = src.FindEvent("Triggered");
        Vector2 outTopLeft, outSize;
        Check(PortGeometry.TryPortBarRect(src, outPort, out outTopLeft, out outSize),
              "connected output bar has a rectangle");
        CheckClose(outTopLeft.Y, cardTopLeft.Y + VgsConstants.NodeHeight,
                   "output bar is flush with the card's bottom edge");
        CheckClose(outTopLeft.X, cardTopLeft.X, "output bar is flush with the card's left edge");
        CheckClose(outSize.X, cardSize.X, "output bar is as wide as the card");

        // The input bar is the same width and the same left edge; only its direction differs.
        Port inPort = dst.FindAction("OnOff");
        Vector2 inTopLeft, inSize;
        Check(PortGeometry.TryPortBarRect(dst, inPort, out inTopLeft, out inSize),
              "connected input bar has a rectangle");
        CheckClose(inTopLeft.X, dst.Position.X, "input bar starts at the card's left edge");
        CheckClose(inSize.X, cardSize.X, "input bar is as wide as the card");
        CheckClose(inSize.X, outSize.X, "input and output bars have the same width");
        CheckClose(inTopLeft.Y, dst.Position.Y + VgsConstants.NodeHeight,
                   "input bar is flush with the card's bottom edge when there is no output stack");

        // Dots: output near the right edge, input near the left edge, both *inside* their bar
        // and one four-pixel gap clear of the border. The horizontal half-extent and the gap are
        // both aspect-corrected, so the check uses the same aspect the caller passes; 1.0 means
        // "square units", i.e. a 1:1 window.
        const float aspect = 1f;
        float inset = ControlOffset(aspect);
        Vector2 outDot, inDot;
        Check(PortGeometry.TryPortDot(src, outPort, aspect, out outDot), "output dot");
        Check(PortGeometry.TryPortDot(dst, inPort, aspect, out inDot), "input dot");
        CheckClose(outDot.X, outTopLeft.X + outSize.X - inset,
                   "output dot is inset from the right edge of its bar");
        CheckClose(inDot.X, inTopLeft.X + inset,
                   "input dot is inset from the left edge of its bar");
        CheckClose(outDot.X + ControlHalfWidth(aspect),
                   outTopLeft.X + outSize.X - VgsConstants.PortControlMargin * aspect,
                   "output dot's edge keeps one gap from the right border");
        CheckClose(inDot.X - ControlHalfWidth(aspect),
                   inTopLeft.X + VgsConstants.PortControlMargin * aspect,
                   "input dot's edge keeps one gap from the left border");
        Check(inDot.X > inTopLeft.X && inDot.X < inTopLeft.X + inSize.X,
              "input dot is inside its bar");
        Check(outDot.X > outTopLeft.X && outDot.X < outTopLeft.X + outSize.X,
              "output dot is inside its bar");

        // A wider window means a wider X unit, so the dot's horizontal extent grows with the
        // aspect — that is what keeps it round instead of oval. Checked across the shapes a
        // player actually has, including one narrower than 4:3.
        float[] dotAspects = { 0.5625f, 0.625f, 0.75f, 1f };
        for (int i = 0; i < dotAspects.Length; i++)
        {
            float a = dotAspects[i];
            Vector2 d;
            Check(PortGeometry.TryPortDot(src, outPort, a, out d), "output dot at aspect " + a);
            CheckClose(d.X, outTopLeft.X + outSize.X - ControlOffset(a),
                       "output dot inset scales with aspect " + a);
            Check(d.X + ControlHalfWidth(a) <= outTopLeft.X + outSize.X + 1e-6f,
                  "output dot stays inside its bar at aspect " + a);
            Check(d.X - ControlHalfWidth(a) >= outTopLeft.X,
                  "output dot does not cross the left edge at aspect " + a);
        }

        Vector2 wideDot;
        Check(PortGeometry.TryPortDot(src, outPort, 2f, out wideDot), "output dot with aspect 2");
        CheckClose(wideDot.X, outTopLeft.X + outSize.X - ControlOffset(2f),
                   "dot inset scales with the aspect");
        Check(wideDot.X < outDot.X, "a wider window pushes the output dot further in");

        // The dot fits vertically inside its bar at every zoom-independent layout: a dot taller
        // than its bar would bleed into the neighbouring port.
        Check(VgsConstants.PortControlRadius * 2f <= VgsConstants.PortBarHeight,
              "dot is no taller than its bar");

        // A node with both stacks: input bars are pushed below the output stack.
        Node both = MakeNode(3, "LargeBlockSensor", SensorToolbar(), "OnOff", "Trigger");
        both.Position = new Vector2(0.6f, 0.1f);
        graph.AddNode(both);
        Check(graph.TryConnect(1, "Triggered", 3, "OnOff", new ToolbarCell(0, 1)), "timer -> sensor OnOff");
        graph.AddNode(MakeTarget(4, "LargeBlockDoor", "Open"));
        Check(graph.TryConnect(3, "Detected#true", 4, "Open", new ToolbarCell(0, 0)),
              "sensor -> door Open");
        graph.LayoutPorts();

        Port bothOut = both.FindEvent("Detected#true");
        Port bothIn = both.FindAction("OnOff");
        Vector2 bothOutTopLeft, bothOutSize, bothInTopLeft, bothInSize;
        Check(PortGeometry.TryPortBarRect(both, bothOut, out bothOutTopLeft, out bothOutSize),
              "sensor output bar");
        Check(PortGeometry.TryPortBarRect(both, bothIn, out bothInTopLeft, out bothInSize),
              "sensor input bar");
        Check(bothInTopLeft.Y >= bothOutTopLeft.Y + bothOutSize.Y,
              "input stack does not overlap the output stack");
        CheckClose(bothInTopLeft.Y,
                   both.Position.Y + VgsConstants.NodeHeight + PortGeometry.OutputStackHeight(both)
                   + VgsConstants.PortStackSpacing,
                   "input stack offset is the output stack plus the stack spacing");

        // With no gap and no spacing the bars are flush: the column has no seams in it, which
        // is the point of stacking them rather than floating them.
        CheckClose(bothInTopLeft.Y, bothOutTopLeft.Y + bothOutSize.Y,
                   "input stack touches the output stack when spacing is zero");

        // Total height covers both stacks.
        float total = PortGeometry.TotalHeight(both);
        Check(total > VgsConstants.NodeHeight + PortGeometry.OutputStackHeight(both),
              "total height includes the input stack");

        // Hit testing: the card and every drawn port bar belong to the node.
        Check(PortGeometry.HitNode(src, src.Position + new Vector2(0.01f, 0.01f)), "card hit");
        Check(PortGeometry.HitNode(src, outTopLeft + new Vector2(0.005f, 0.005f)), "port bar hit");
        Check(!PortGeometry.HitNode(src, src.Position + new Vector2(-0.05f, 0f)), "miss to the left");

        // The pending dot of a fresh bind is where the bar will appear.
        Node fresh = MakeNode(6, "LargeBlockTimerBlock");
        Vector2 pending;
        Check(PortGeometry.TryPendingSourceDot(fresh, fresh.PrimaryEvent, aspect, out pending),
              "pending source dot exists");
        CheckClose(pending.Y, fresh.Position.Y + VgsConstants.NodeHeight + VgsConstants.PortBarHeight * 0.5f,
                   "pending dot sits at the centre of the first output slot");
        CheckClose(pending.X, fresh.Position.X + VgsConstants.PortBarWidth - inset,
                   "pending dot is inset like a drawn output dot");

        // And when the port is already drawn, the pending dot is the drawn dot.
        Vector2 pendingDrawn;
        Check(PortGeometry.TryPendingSourceDot(src, src.PrimaryEvent, aspect, out pendingDrawn),
              "pending dot for an already-drawn port");
        CheckClose(pendingDrawn.Y, outDot.Y, "pending dot coincides with the drawn output dot");
        CheckClose(pendingDrawn.X, outDot.X, "pending dot coincides with the drawn output dot (x)");
    }

    /// <summary>
    /// The card's button row (ADR-0020, ADR-0021).
    ///
    /// Every control that belongs to the *card* — the jump-to-terminal button, the single bind
    /// button, the yes/no pair and a controller's watch button — sits in one horizontal row on the
    /// card's right edge, vertically centred, all at one size. What matters is that each button
    /// exists exactly where it can be clicked, that the row stays inside the card, that two cards
    /// show the same button size whatever their toolbars are, that the jump button is present on
    /// every block and on no group, and that a click in the gap between two buttons lands on the
    /// nearer one instead of on whichever was tested first.
    /// </summary>
    private static void CardButtonRow()
    {
        const float aspect = 1f;

        Node timer = MakeNode(1, "LargeBlockTimerBlock");
        timer.Position = new Vector2(0.2f, 0.2f);

        Node sensor = MakeNode(2, "LargeBlockSensor", SensorToolbar());
        sensor.Position = new Vector2(0.2f, 0.4f);

        var controller = new Node(3, "EventControllerLarge", "controller", Vector3I.Zero,
                                  SensorToolbar(), true);
        BlockPortCatalog.FillEvents(controller);
        controller.Position = new Vector2(0.2f, 0.7f);

        Node conveyor = MakeTarget(4, "LargeBlockConveyorTube");

        // ---- which card carries which buttons ----

        // The jump button is on every block, including one that cannot drive anything and has no
        // toolbar at all: it opens the block's own terminal, which every terminal block has
        // (ADR-0021).
        Check(PortGeometry.HasCardButton(timer, CardButtonKind.Jump),
              "a timer has the jump button");
        Check(PortGeometry.HasCardButton(conveyor, CardButtonKind.Jump),
              "a block with no toolbar still has the jump button");

        Check(PortGeometry.CardButtonCount(timer) == 2,
              "a one-group block has the jump and the single bind button");
        Check(PortGeometry.HasCardButton(timer, CardButtonKind.BindSingle),
              "that second button is the single bind button");
        Check(!PortGeometry.HasCardButton(timer, CardButtonKind.BindYes),
              "a one-group block has no yes button");
        Check(!PortGeometry.HasCardButton(timer, CardButtonKind.Watch),
              "a timer has no watch button");

        Check(PortGeometry.CardButtonCount(sensor) == 3,
              "a yes/no block has the jump and the pair");
        Check(PortGeometry.HasCardButton(sensor, CardButtonKind.BindYes), "the yes button exists");
        Check(PortGeometry.HasCardButton(sensor, CardButtonKind.BindNo), "the no button exists");
        Check(!PortGeometry.HasCardButton(sensor, CardButtonKind.BindSingle),
              "a yes/no block has no single button — the group must be chosen first");
        Check(!PortGeometry.HasCardButton(sensor, CardButtonKind.Watch),
              "a sensor is not a controller, so it has no watch button");

        Check(PortGeometry.CardButtonCount(controller) == 4,
              "a controller shows the jump, the watch button and the pair");
        Check(PortGeometry.HasCardButton(controller, CardButtonKind.Watch),
              "the controller has a watch button");

        Check(PortGeometry.CardButtonCount(conveyor) == 1,
              "a block without a toolbar has the jump button and nothing else");

        // A group is a frame, not a block: it has no terminal, so the jump would have nowhere to
        // go. This is the one card-like thing that must not show the button.
        var group = new Node("Grp", "any");
        group.Position = new Vector2(0.5f, 0.5f);
        Check(!PortGeometry.HasCardButton(group, CardButtonKind.Jump),
              "a group frame has no jump button — a group has no terminal");
        Check(PortGeometry.CardButtonCount(group) == 0, "a group frame has no buttons at all");

        // ---- the row's order ----

        // Jump first, then the rest: the button that leaves the schema is the one a reader of a
        // card looks for first, and putting it in front keeps every later button where ADR-0020
        // put it.
        Check(PortGeometry.CardButtonIndex(timer, CardButtonKind.Jump) == 0,
              "the jump button is the first of the row");
        Check(PortGeometry.CardButtonIndex(timer, CardButtonKind.BindSingle) == 1,
              "the single bind button follows the jump");
        Check(PortGeometry.CardButtonIndex(controller, CardButtonKind.Jump) == 0,
              "the jump button is the first of a controller's row too");
        Check(PortGeometry.CardButtonIndex(controller, CardButtonKind.Watch) == 1,
              "the watch button is the second of the row");
        Check(PortGeometry.CardButtonIndex(controller, CardButtonKind.BindYes) == 2,
              "the yes button is the third");
        Check(PortGeometry.CardButtonIndex(controller, CardButtonKind.BindNo) == 3,
              "the no button is the fourth");

        // ---- one row, one size, on the right ----

        Node[] nodes = { timer, sensor, controller, conveyor };
        for (int n = 0; n < nodes.Length; n++)
        {
            Node node = nodes[n];
            int count = PortGeometry.CardButtonCount(node);
            float cardRight = node.Position.X + VgsConstants.NodeWidth;
            float cardCentreY = node.Position.Y + PortGeometry.CardHeight(node) * 0.5f;

            Vector2 previousTopLeft = Vector2.Zero;
            Vector2 previousSize = Vector2.Zero;
            bool havePrevious = false;

            for (int i = 0; i < count; i++)
            {
                CardButtonKind kind;
                Check(PortGeometry.TryCardButtonAt(node, i, out kind),
                      "button " + i + " of the row has a kind");

                Vector2 topLeft, size;
                Check(PortGeometry.TryCardButtonRect(node, kind, aspect, out topLeft, out size),
                      "button " + kind + " of " + node.SubtypeId + " has a rectangle");

                Vector2 centre;
                Check(PortGeometry.TryCardButton(node, kind, aspect, out centre),
                      "button " + kind + " of " + node.SubtypeId + " has a centre");
                CheckClose(centre.X, topLeft.X + size.X * 0.5f, "the centre is the rectangle's centre in X");
                CheckClose(centre.Y, topLeft.Y + size.Y * 0.5f, "the centre is the rectangle's centre in Y");

                // One size for every button on every card. A controller's card is one line
                // taller, and its buttons must not grow with it: two cards side by side showing
                // two button sizes is exactly the state this row replaced.
                CheckClose(size.Y, VgsConstants.CardButtonDiameter,
                           "every card button has the standard diameter (" + kind + ")");
                CheckClose(size.X, VgsConstants.CardButtonDiameter * aspect,
                           "every card button is as wide as it is tall at aspect 1 (" + kind + ")");

                // One row: every button shares the card's vertical centre line.
                CheckClose(centre.Y, cardCentreY,
                           "button " + kind + " is vertically centred on its card");

                // Inside the card, at all four edges.
                Check(topLeft.X >= node.Position.X - 1e-6f,
                      "button " + kind + " does not leave the card at the left");
                Check(topLeft.X + size.X <= cardRight + 1e-6f,
                      "button " + kind + " does not leave the card at the right");
                Check(topLeft.Y >= node.Position.Y - 1e-6f,
                      "button " + kind + " does not leave the card at the top");
                Check(topLeft.Y + size.Y <= node.Position.Y + PortGeometry.CardHeight(node) + 1e-6f,
                      "button " + kind + " does not leave the card at the bottom");

                // The row is right-anchored and flush: the last button's right edge stands one
                // four-pixel gap from the card's border.
                if (i == count - 1)
                {
                    CheckClose(topLeft.X + size.X,
                               cardRight - VgsConstants.CardButtonMargin * aspect,
                               "the rightmost button keeps one gap from the card's right border");
                }

                // Neighbours are separated by the row gap, never touching.
                if (havePrevious)
                {
                    float gap = topLeft.X - (previousTopLeft.X + previousSize.X);
                    CheckClose(gap, VgsConstants.CardButtonGap * aspect,
                               "neighbouring buttons are one row gap apart (" + kind + ")");
                }

                previousTopLeft = topLeft;
                previousSize = size;
                havePrevious = true;
            }
        }

        // The controller's card really is taller, and its buttons really are not: that is the
        // unification, stated as a comparison rather than as a constant.
        Check(PortGeometry.CardHeight(controller) > PortGeometry.CardHeight(timer),
              "a controller's card is taller than an ordinary one");
        Vector2 timerButton, controllerButton, timerSize, controllerSize, unusedTopLeft;
        Check(PortGeometry.TryCardButton(timer, CardButtonKind.BindSingle, aspect, out timerButton),
              "timer button centre");
        Check(PortGeometry.TryCardButton(controller, CardButtonKind.BindYes, aspect, out controllerButton),
              "controller button centre");
        Check(PortGeometry.TryCardButtonRect(timer, CardButtonKind.BindSingle, aspect,
                                             out unusedTopLeft, out timerSize),
              "timer button rectangle");
        Check(PortGeometry.TryCardButtonRect(controller, CardButtonKind.BindYes, aspect,
                                             out unusedTopLeft, out controllerSize),
              "controller button rectangle");
        CheckClose(controllerSize.Y, timerSize.Y,
                   "the same button is the same size on a taller card");
        CheckClose(controllerSize.X, timerSize.X,
                   "and the same width, so the pair reads as one control on every card");

        // ---- the row as data: one pass, and the two ways of asking agree ----
        //
        // The draw path asks for the whole row at once (`GetCardButtons`) and then places each
        // button by its position (`CardButtonRectAt`); everything else asks by kind
        // (`CardButtonIndex` / `TryCardButtonAt` / `TryCardButtonRect`). Two ways of asking the
        // same question is exactly how a button ends up drawn in one place and clicked in
        // another, so they are compared here rather than trusted.

        var rowBuffer = new CardButtonKind[PortGeometry.MaxCardButtonCount];

        // The buffer a caller sizes once is derived from the row itself, but the *enum* could still
        // grow a member that no card ever shows — and then a future button would be silently
        // truncated by the buffer while hit-testing kept finding it. Pinning the two together is
        // what turns that into a red gate instead of an invisible, clickable button.
        Check(PortGeometry.MaxCardButtonCount
              == Enum.GetValues(typeof(CardButtonKind)).Length,
              "MaxCardButtonCount covers every CardButtonKind the enum declares");
        Check(PortGeometry.MaxCardButtonCount >= 4,
              "the row can hold the widest card (jump, watch, yes, no)");

        // Every kind of button maps to a bind group or to none, and the mapping is the one the
        // click dispatcher depends on: a swapped Yes/No would pass every other check here.
        PortGroup mapped;
        Check(PortGeometry.TryGroupOf(CardButtonKind.BindYes, out mapped) && mapped == PortGroup.Yes,
              "the yes button binds the yes group");
        Check(PortGeometry.TryGroupOf(CardButtonKind.BindNo, out mapped) && mapped == PortGroup.No,
              "the no button binds the no group");
        Check(PortGeometry.TryGroupOf(CardButtonKind.BindSingle, out mapped)
              && mapped == PortGroup.Single,
              "the single bind button binds the single group");
        Check(!PortGeometry.TryGroupOf(CardButtonKind.Jump, out mapped),
              "the jump button binds nothing — it leaves the schema");
        Check(!PortGeometry.TryGroupOf(CardButtonKind.Watch, out mapped),
              "the watch button binds nothing — it starts a watch");

        // ---- the row is null-safe: it is called from the draw path, where a throw kills the frame

        CardButtonKind nullKind;
        Check(PortGeometry.CardButtonCount(null) == 0, "a null node has no buttons");
        Check(!PortGeometry.HasCardButton(null, CardButtonKind.Jump), "a null node has no jump button");
        Check(PortGeometry.CardButtonIndex(null, CardButtonKind.Jump) == -1, "a null node has no index");
        Check(!PortGeometry.TryCardButtonAt(null, 0, out nullKind), "a null node has no button at 0");
        Check(PortGeometry.GetCardButtons(null, rowBuffer) == 0, "a null node fills no buffer");
        Check(PortGeometry.GetCardButtons(timer, null) == 0, "a null buffer is refused, not written");
        Vector2 nullTopLeft, nullSize;
        PortGeometry.CardButtonRectAt(null, 0, 1, aspect, out nullTopLeft, out nullSize);
        CheckClose(nullSize.X, 0f, "a null node's button rectangle is empty (width)");
        CheckClose(nullSize.Y, 0f, "a null node's button rectangle is empty (height)");

        // Out-of-range positions are refused, not clamped onto a neighbour.
        CardButtonKind outOfRange;
        Check(!PortGeometry.TryCardButtonAt(null, -1, out outOfRange),
              "position -1 on a null node is not a button");
        PortGeometry.CardButtonRectAt(controller, -1, 4, aspect, out nullTopLeft, out nullSize);
        CheckClose(nullSize.Y, 0f, "a negative position gives an empty rectangle");
        PortGeometry.CardButtonRectAt(controller, 4, 4, aspect, out nullTopLeft, out nullSize);
        CheckClose(nullSize.Y, 0f, "a position past the row gives an empty rectangle");
        PortGeometry.CardButtonRectAt(controller, 0, 0, aspect, out nullTopLeft, out nullSize);
        CheckClose(nullSize.Y, 0f, "a zero-length row gives an empty rectangle");

        // A group has no buttons at all, so nothing on its frame is clickable.
        CardButtonKind groupHit;
        Check(!PortGeometry.HitCardButton(group, group.Position, aspect, out groupHit),
              "nothing on a group's frame is a card button");
        Vector2 groupRowTopLeft, groupRowSize;
        Check(!PortGeometry.TryCardButtonRow(group, aspect, out groupRowTopLeft, out groupRowSize),
              "a group's frame has no button row");

        // A stale node — a block that was destroyed but whose card is still on the canvas until the
        // next refresh — still carries the jump button. That is what makes the "block is gone"
        // refusal reachable instead of the button vanishing mid-click.
        var stale = new Node(0L, "LargeBlockTimerBlock", "gone", Vector3I.Zero);
        Check(PortGeometry.HasCardButton(stale, CardButtonKind.Jump),
              "a card whose block id is zero still shows the jump button");

        Node[] rowNodes = { timer, sensor, controller, conveyor, group };
        for (int n = 0; n < rowNodes.Length; n++)
        {
            Node node = rowNodes[n];
            int count = PortGeometry.GetCardButtons(node, rowBuffer);

            Check(count == PortGeometry.CardButtonCount(node),
                  "GetCardButtons and CardButtonCount agree (" + node.SubtypeId + ")");
            Check(count <= PortGeometry.MaxCardButtonCount,
                  "the row fits the buffer a caller can size once (" + node.SubtypeId + ")");

            // Every position of the row has the kind the by-index and by-kind lookups report, and
            // each kind appears exactly once.
            for (int i = 0; i < count; i++)
            {
                CardButtonKind fromBuffer = rowBuffer[i];

                CardButtonKind fromIndex;
                Check(PortGeometry.TryCardButtonAt(node, i, out fromIndex),
                      "position " + i + " exists by index (" + node.SubtypeId + ")");
                Check(fromBuffer == fromIndex,
                      "GetCardButtons and TryCardButtonAt agree at " + i + " (" + node.SubtypeId + ")");
                Check(PortGeometry.CardButtonIndex(node, fromBuffer) == i,
                      "CardButtonIndex inverts TryCardButtonAt at " + i + " (" + node.SubtypeId + ")");

                // The two ways of placing a button give the same rectangle.
                Vector2 byKind, byKindSize, byPos, byPosSize;
                Check(PortGeometry.TryCardButtonRect(node, fromBuffer, aspect, out byKind, out byKindSize),
                      "by-kind rectangle at " + i + " (" + node.SubtypeId + ")");
                PortGeometry.CardButtonRectAt(node, i, count, aspect, out byPos, out byPosSize);
                CheckClose(byKind.X, byPos.X, "both placements agree in X at " + i + " (" + node.SubtypeId + ")");
                CheckClose(byKind.Y, byPos.Y, "both placements agree in Y at " + i + " (" + node.SubtypeId + ")");
                CheckClose(byKindSize.X, byPosSize.X, "both placements agree in width at " + i + " (" + node.SubtypeId + ")");
                CheckClose(byKindSize.Y, byPosSize.Y, "both placements agree in height at " + i + " (" + node.SubtypeId + ")");
            }

            // A kind that is not in the row has no index and no rectangle. The list of kinds is
            // written out rather than derived from the enum's numeric values: those values are
            // deliberately not row positions (see the enum's own comment), and iterating them by
            // number would quietly weaken this check the day they are renumbered.
            CardButtonKind[] allKinds =
            {
                CardButtonKind.Jump, CardButtonKind.BindSingle, CardButtonKind.BindYes,
                CardButtonKind.BindNo, CardButtonKind.Watch,
            };
            CardButtonKind absent = CardButtonKind.Jump;
            bool anyAbsent = false;
            for (int k = 0; k < allKinds.Length; k++)
            {
                CardButtonKind candidate = allKinds[k];
                bool present = false;
                for (int i = 0; i < count; i++) if (rowBuffer[i] == candidate) present = true;
                Check(PortGeometry.HasCardButton(node, candidate) == present,
                      "HasCardButton agrees with the row it produces (" + candidate + ", "
                      + node.SubtypeId + ")");
                if (!present)
                {
                    absent = candidate;
                    anyAbsent = true;
                }
            }
            if (anyAbsent)
            {
                Check(PortGeometry.CardButtonIndex(node, absent) == -1,
                      "a button the card does not carry has no index (" + node.SubtypeId + ")");
                Vector2 t, s;
                Check(!PortGeometry.TryCardButtonRect(node, absent, aspect, out t, out s),
                      "a button the card does not carry has no rectangle (" + node.SubtypeId + ")");
            }

            // Out-of-range positions are refused, not clamped onto a neighbour.
            CardButtonKind pastRow;
            Check(!PortGeometry.TryCardButtonAt(node, -1, out pastRow),
                  "position -1 is not a button (" + node.SubtypeId + ")");
            Check(!PortGeometry.TryCardButtonAt(node, count, out pastRow),
                  "the position one past the row is not a button (" + node.SubtypeId + ")");
        }

        // ---- the row leaves room for the name ----

        for (int n = 0; n < nodes.Length; n++)
        {
            Node node = nodes[n];
            Vector2 rowTopLeft, rowSize;
            Check(PortGeometry.TryCardButtonRow(node, aspect, out rowTopLeft, out rowSize),
                  "the row has a rectangle (" + node.SubtypeId + ")");

            int count = PortGeometry.CardButtonCount(node);
            CheckClose(rowSize.X, count * VgsConstants.CardButtonDiameter * aspect
                                  + (count - 1) * VgsConstants.CardButtonGap * aspect,
                       "the row is exactly as wide as its buttons and gaps");
            CheckClose(rowTopLeft.Y, node.Position.Y + PortGeometry.CardHeight(node) * 0.5f
                                     - VgsConstants.CardButtonDiameter * 0.5f,
                       "the row is centred on the card");

            // The name starts inside the card and has to reach the row with a gap to spare. The
            // floor is 30% of the card's width: a card whose text span collapsed would show
            // buttons and no name, and the four-button card is the one that could do it — so
            // this is the assertion that keeps the row from growing without bound.
            float spanLeft, spanRight;
            Check(PortGeometry.TryCardTextSpan(node, aspect, out spanLeft, out spanRight),
                  "the card's text span is defined (" + node.SubtypeId + ")");
            Check(spanRight > spanLeft, "the text span is not inverted (" + node.SubtypeId + ")");
            Check(spanRight - spanLeft > VgsConstants.NodeWidth * 0.3f,
                  "the row leaves the card's name at least 30% of the card's width ("
                  + node.SubtypeId + ")");
            CheckClose(spanRight, rowTopLeft.X - VgsConstants.CardButtonTextGap * aspect,
                       "the text span ends one gap left of the row");
            CheckClose(spanLeft, node.Position.X + VgsConstants.NodeWidth
                                 * PortGeometry.CardTextPaddingRatio,
                       "the text span starts at the card's inner padding");
        }

        // ---- the budget for the fourth button (ADR-0021) ----
        //
        // The controller's row is the widest one the editor can produce: jump, watch, "yes",
        // "no". It is asserted through the real node, not through a hypothetical count, because
        // that card exists — and it is the one the button diameter was reduced for.
        //
        // The aspect matters more than it looks. A button is round on screen, so its horizontal
        // extent is radius × aspect: the *wider* the window, the *narrower* the row relative to
        // the card. A window taller than it is wide is therefore the worst case by a wide
        // margin, and it is the shape the previous diameter could not pay for: at 0.75 of the
        // card height the four-button row filled the card at aspect 1.21 and left the card at
        // 1.33; at 0.60 those limits move to 1.46 and 1.62 (ADR-0021).
        {
            // Everything below is derived from the constants, so a change to the diameter, the
            // gaps or the card's width moves the limits and the assertions with them.
            int widest = PortGeometry.CardButtonCount(controller);
            Check(widest == 4, "the controller's row is the widest one: four buttons");

            float rowWidth = widest * VgsConstants.CardButtonDiameter
                             + (widest - 1) * VgsConstants.CardButtonGap;
            float textLeft = VgsConstants.NodeWidth * PortGeometry.CardTextPaddingRatio;
            float horizontalOverhead = VgsConstants.CardButtonMargin
                                       + rowWidth
                                       + VgsConstants.CardButtonTextGap;

            // Aspect at which the row exactly fills the card: the name is gone but every button
            // is still inside its card. Above this the leftmost button would leave the card.
            float fillsCard = (VgsConstants.NodeWidth - textLeft) / horizontalOverhead;
            // Aspect at which the name still keeps its 30% floor.
            float keepsName = (VgsConstants.NodeWidth - textLeft - VgsConstants.NodeWidth * 0.3f)
                              / horizontalOverhead;

            // <b>This block reproduces ADR-0021's numbers, and it is the "no state column"
            // baseline on purpose.</b> The controller node here is built without a switch
            // (`CanBeEnabled` left at its default), so the nameplate starts at the plain inner
            // padding. That combination no longer occurs in the game — an event controller is an
            // `IMyFunctionalBlock` and therefore always has a column — but the arithmetic is what
            // ADR-0021 recorded, and keeping it here is what lets the two documents be compared.
            // The real, switch-bearing case is asserted in `PowerSwitch` below, where the column
            // costs the nameplate the difference between 1.00 and 0.78.

            // The three shapes the editor is used in keep the name, and the fourth — a square
            // window, which is legal but rare — keeps it too, exactly at the floor.
            float[] nameAspects = { 0.5625f, 0.625f, 0.75f, 1f };   // 16:9, 16:10, 4:3, square
            for (int i = 0; i < nameAspects.Length; i++)
            {
                float a = nameAspects[i];
                Vector2 t, s;
                float spanLeft, spanRight;
                Check(PortGeometry.TryCardButtonRect(controller, CardButtonKind.Jump, a, out t, out s),
                      "jump button rectangle at aspect " + a);
                Check(t.X >= controller.Position.X - 1e-5f,
                      "the four-button row does not leave the card on the left at aspect " + a);
                Check(PortGeometry.TryCardTextSpan(controller, a, out spanLeft, out spanRight),
                      "the four-button row still leaves a name at aspect " + a);
                Check(spanRight - spanLeft >= VgsConstants.NodeWidth * 0.3f - 1e-5f,
                      "the four-button row leaves the name 30% of the card at aspect " + a);
            }

            // The limits themselves are part of the decision, so they are asserted rather than
            // left as prose: a future change to the button size that quietly narrows them would
            // otherwise only show up as a squeezed name in a window nobody tests in.
            Check(fillsCard >= 1.45f,
                  "a four-button row still fits inside the card up to aspect 1.45 (is " + fillsCard + ")");
            Check(keepsName >= 0.95f,
                  "a four-button row keeps the name up to aspect 0.95 (is " + keepsName + ")");

            // Portrait windows are legal and the row must not fall out of the card there either:
            // 1.6 is the shape the editor already tolerated with three buttons, and the reduced
            // diameter is what keeps four buttons inside it.
            {
                const float a = 1.6f;
                Vector2 leftTopLeft, leftSize, rightTopLeft, rightSize;
                Check(PortGeometry.TryCardButtonRect(controller, CardButtonKind.Jump, a,
                                                     out leftTopLeft, out leftSize),
                      "jump button rectangle at aspect 1.6");
                Check(PortGeometry.TryCardButtonRect(controller, CardButtonKind.BindNo, a,
                                                     out rightTopLeft, out rightSize),
                      "no button rectangle at aspect 1.6");

                // The leftmost button is the one that would leave the card, so it is the one the
                // assertion is about; the rightmost is checked too, because it is the row's anchor.
                Check(leftTopLeft.X >= controller.Position.X - 1e-5f,
                      "the four-button row does not leave the card on the left at aspect 1.6");
                Check(rightTopLeft.X + rightSize.X
                      <= controller.Position.X + VgsConstants.NodeWidth + 1e-5f,
                      "the rightmost button is still inside the card at aspect 1.6");

                // The name is gone at this shape — that is the documented cost, not an accident
                // (ADR-0021 §Consequences): the card shows its four controls and no text.
                float spanL, spanR;
                Check(!PortGeometry.TryCardTextSpan(controller, a, out spanL, out spanR),
                      "at aspect 1.6 the four-button card has no room left for a name");
            }

            // Buttons have to stay visibly separate without the gaps eating the row: a gap of at
            // most half a radius, and at least one reference pixel so the marks never touch.
            Check(VgsConstants.CardButtonGap <= VgsConstants.CardButtonRadius * 0.5f,
                  "the gap between two buttons is at most half a button radius");
            Check(VgsConstants.CardButtonGapPixels >= 1f,
                  "the gap between two buttons is at least one pixel at the reference height");

            // The stride must exceed the button, or two hit targets would have the same centre.
            Check(VgsConstants.CardButtonDiameter + VgsConstants.CardButtonGap
                  > VgsConstants.CardButtonDiameter,
                  "two neighbouring buttons are further apart than one button");
        }

        // ---- clicking ----

        CardButtonKind hit;

        Vector2 timerCentre;
        Check(PortGeometry.TryCardButton(timer, CardButtonKind.BindSingle, aspect, out timerCentre),
              "timer button centre for the hit test");
        Check(PortGeometry.HitCardButton(timer, timerCentre, aspect, out hit), "the bind button hits");
        Check(hit == CardButtonKind.BindSingle, "the bind button reports its own kind");
        Check(!PortGeometry.HitCardButton(timer, timerCentre + new Vector2(0.05f, 0f), aspect, out hit),
              "far to the right of the row is a miss");

        // A square icon's corners reach further from its centre than its sides do. The hit target
        // has to cover them, or the corners would look pressable and do nothing (ADR-0019 §4a).
        float bindRadius = VgsConstants.CardButtonRadius;
        var cornerOffset = new Vector2(VgsConstants.CardButtonIconRatio * bindRadius,
                                       VgsConstants.CardButtonIconRatio * bindRadius);
        Check(PortGeometry.HitCardButton(timer, timerCentre + cornerOffset, aspect, out hit),
              "the drawn corner of the square-plus icon is inside the hit target");
        Check(PortGeometry.HitCardButton(timer, timerCentre - cornerOffset, aspect, out hit),
              "the opposite corner is inside the hit target too");

        // ...and the tolerance is bounded: a point well beyond the button is still a miss.
        var farCorner = new Vector2(bindRadius * 2f, bindRadius * 2f);
        Check(!PortGeometry.HitCardButton(timer, timerCentre + farCorner, aspect, out hit),
              "well outside the button is a miss, so the tolerance is still bounded");

        Vector2 yesCentre, noCentre;
        Check(PortGeometry.TryCardButton(sensor, CardButtonKind.BindYes, aspect, out yesCentre),
              "yes button centre");
        Check(PortGeometry.TryCardButton(sensor, CardButtonKind.BindNo, aspect, out noCentre),
              "no button centre");
        Check(yesCentre.X < noCentre.X, "the yes button is left of the no button");
        CheckClose(yesCentre.Y, noCentre.Y, "both buttons of the pair sit on one line");

        Check(PortGeometry.HitCardButton(sensor, yesCentre, aspect, out hit) && hit == CardButtonKind.BindYes,
              "clicking the yes button reports the yes group");
        Check(PortGeometry.HitCardButton(sensor, noCentre, aspect, out hit) && hit == CardButtonKind.BindNo,
              "clicking the no button reports the no group");

        // The hit targets overlap at this tolerance, so the nearer centre has to win: a click in
        // the gap must not depend on the order the buttons happen to be tested in.
        var between = new Vector2((yesCentre.X + noCentre.X) * 0.5f, yesCentre.Y);
        Check(PortGeometry.HitCardButton(sensor, between, aspect, out hit),
              "the gap between two buttons is still a hit, not a dead zone");
        Check(hit == CardButtonKind.BindNo,
              "a click exactly between two buttons lands on the later one, deterministically");

        var nearerNo = new Vector2(yesCentre.X + (noCentre.X - yesCentre.X) * 0.75f, yesCentre.Y);
        Check(PortGeometry.HitCardButton(sensor, nearerNo, aspect, out hit)
              && hit == CardButtonKind.BindNo,
              "a click nearer the no button reports the no button");
        var nearerYes = new Vector2(yesCentre.X + (noCentre.X - yesCentre.X) * 0.25f, yesCentre.Y);
        Check(PortGeometry.HitCardButton(sensor, nearerYes, aspect, out hit)
              && hit == CardButtonKind.BindYes,
              "a click nearer the yes button reports the yes button");

        // The watch button follows the jump button in the controller's row, and it is the point a
        // watch line starts from — the port does not exist until a block is attached.
        Vector2 watchCentre, controllerYes, controllerJump;
        Check(PortGeometry.TryWatchButton(controller, aspect, out watchCentre),
              "the controller's watch button has a centre");
        Check(PortGeometry.HitCardButton(controller, watchCentre, aspect, out hit)
              && hit == CardButtonKind.Watch,
              "clicking the bell reports the watch button");
        Check(PortGeometry.CardButtonIndex(controller, CardButtonKind.Watch) == 1,
              "the watch button is the second of the row, right after the jump button");
        Check(PortGeometry.TryCardButton(controller, CardButtonKind.BindYes, aspect, out controllerYes),
              "the controller's yes button has a centre");
        Check(PortGeometry.TryCardButton(controller, CardButtonKind.Jump, aspect, out controllerJump),
              "the controller's jump button has a centre");
        Check(controllerJump.X < watchCentre.X,
              "the jump button is left of the bell, so the row reads jump → watch → yes → no");
        Check(watchCentre.X < controllerYes.X,
              "the watch button is left of the bind pair, so the row reads watch → yes → no");

        // The jump button is the first control of every block card, so it is the leftmost — and it
        // is the one button whose click does not start a mode (ADR-0021).
        Vector2 jumpCentre;
        Check(PortGeometry.TryCardButton(timer, CardButtonKind.Jump, aspect, out jumpCentre),
              "the timer's jump button has a centre");
        Check(PortGeometry.HitCardButton(timer, jumpCentre, aspect, out hit)
              && hit == CardButtonKind.Jump,
              "clicking the arrow reports the jump button");
        Check(jumpCentre.X < timerCentre.X, "the jump button is left of the single bind button");
        PortGroup jumpGroup;
        Check(!PortGeometry.TryGroupOf(CardButtonKind.Jump, out jumpGroup),
              "the jump button starts no bind group — it leaves the schema instead");
        Check(!PortGeometry.TryGroupOf(CardButtonKind.Watch, out jumpGroup),
              "the watch button starts no bind group either");

        // ---- aspect independence ----

        // A button is round on screen, so its extent and its distance from the card's border are
        // both vertical lengths turned into horizontal ones. Without that correction the row
        // would look inset on a wide monitor and flush on a 4:3 one.
        float[] aspects = { 0.5625f, 0.75f, 1f, 1.6f };
        for (int i = 0; i < aspects.Length; i++)
        {
            float a = aspects[i];

            Vector2 t, s;
            Check(PortGeometry.TryCardButtonRect(sensor, CardButtonKind.BindNo, a, out t, out s),
                  "no button rectangle at aspect " + a);
            CheckClose(s.X / a, s.Y, "the button is square on screen at aspect " + a);
            CheckClose(t.X + s.X,
                       sensor.Position.X + VgsConstants.NodeWidth - VgsConstants.CardButtonMargin * a,
                       "the button's right edge is a fixed distance from the border at aspect " + a);
            Check(t.X + s.X <= sensor.Position.X + VgsConstants.NodeWidth + 1e-5f,
                  "the button stays inside the card at aspect " + a);

            // The pair's pitch scales with the aspect too, or the gap would read as a different
            // number of pixels on every window shape.
            Vector2 y, ny;
            Check(PortGeometry.TryCardButton(sensor, CardButtonKind.BindYes, a, out y),
                  "yes button at aspect " + a);
            Check(PortGeometry.TryCardButton(sensor, CardButtonKind.BindNo, a, out ny),
                  "no button at aspect " + a);
            CheckClose((ny.X - y.X) / a, VgsConstants.CardButtonDiameter + VgsConstants.CardButtonGap,
                       "the pair's pitch is aspect-independent at aspect " + a);
        }

        // ---- the row is the whole of a card's controls ----

        Check(!PortGeometry.HasGroupButtons(timer), "a one-group block shows no yes/no pair");
        Check(PortGeometry.HasGroupButtons(sensor), "a yes/no block shows the pair");
        Check(PortGeometry.HasGroupButtons(controller), "a controller shows the pair too");
    }

    /// <summary>
    /// The two round controls on a port bar: the connection dot and the remove "x".
    ///
    /// They are the same size by construction and mirror images of each other, and both stand
    /// four screen pixels off the bar's border (ADR-0014). These are the properties a player
    /// notices immediately when they are wrong — a dot clipped by its border, a button that is
    /// not where the dot is, a gap that changes with the window's shape — and none of them is
    /// visible in a compile.
    /// </summary>
    private static void PortControls()
    {
        var graph = new GridGraph(11);
        Node src = MakeNode(1, "LargeBlockTimerBlock");
        Node dst = MakeTarget(2, "LargeBlockSmallGenerator", "OnOff");
        src.Position = new Vector2(0.2f, 0.2f);
        dst.Position = new Vector2(0.2f, 0.5f);
        graph.AddNode(src);
        graph.AddNode(dst);
        Check(graph.TryConnect(1, "Triggered", 2, "OnOff", new ToolbarCell(0, 0)), "connect for controls");
        graph.LayoutPorts();

        Port outPort = src.FindEvent("Triggered");
        Port inPort = dst.FindAction("OnOff");

        Vector2 outTopLeft, outSize, inTopLeft, inSize;
        Check(PortGeometry.TryPortBarRect(src, outPort, out outTopLeft, out outSize), "output bar");
        Check(PortGeometry.TryPortBarRect(dst, inPort, out inTopLeft, out inSize), "input bar");

        // One ratio for both controls: the remove button is the dot's mirror, so a different
        // size would read as two unrelated elements on one bar.
        CheckClose(VgsConstants.PortControlRadius * 2f, VgsConstants.PortBarHeight * 0.75f,
                   "a port control is 75% of the bar's height");
        Check(VgsConstants.PortControlRadius * 2f <= VgsConstants.PortBarHeight,
              "a port control is no taller than its bar");

        // Four screen pixels. The constant is a vertical length, so on a 16:9 window (aspect
        // 0.5625) the horizontal gap is 4/1920 of the width — the same four pixels, not four
        // layout units, which would be seven.
        const float aspect16By9 = 0.5625f;
        float gapX = VgsConstants.PortControlMargin * aspect16By9;
        CheckClose(gapX * 1920f, 4f, "the gap is four pixels wide at 1920x1080");

        float half = ControlHalfWidth(aspect16By9);

        Vector2 outDot, outRemove, inDot, inRemove;
        Check(PortGeometry.TryPortDot(src, outPort, aspect16By9, out outDot), "output dot");
        Check(PortGeometry.TryPortRemoveButton(src, outPort, aspect16By9, out outRemove), "output x");
        Check(PortGeometry.TryPortDot(dst, inPort, aspect16By9, out inDot), "input dot");
        Check(PortGeometry.TryPortRemoveButton(dst, inPort, aspect16By9, out inRemove), "input x");

        // The dot stays on the side the line uses; the remove button takes the other side.
        CheckClose(outDot.X, outTopLeft.X + outSize.X - ControlOffset(aspect16By9),
                   "output dot is inset from the right border");
        CheckClose(outRemove.X, outTopLeft.X + ControlOffset(aspect16By9),
                   "output x is inset from the left border");
        CheckClose(inDot.X, inTopLeft.X + ControlOffset(aspect16By9),
                   "input dot is inset from the left border");
        CheckClose(inRemove.X, inTopLeft.X + inSize.X - ControlOffset(aspect16By9),
                   "input x is inset from the right border");

        // The gap is measured from the disc's *edge*, not from its centre: this is the property
        // the requirement names, and the one a "radius + margin" mix-up breaks while leaving the
        // centre looking plausible.
        CheckClose(outDot.X + half, outTopLeft.X + outSize.X - gapX,
                   "the output dot's edge is four pixels from the border");
        CheckClose(outRemove.X - half, outTopLeft.X + gapX,
                   "the output x's edge is four pixels from the border");
        CheckClose(inDot.X - half, inTopLeft.X + gapX,
                   "the input dot's edge is four pixels from the border");
        CheckClose(inRemove.X + half, inTopLeft.X + inSize.X - gapX,
                   "the input x's edge is four pixels from the border");

        // Both are centred on the bar's height, whatever their side.
        CheckClose(outDot.Y, outTopLeft.Y + outSize.Y * 0.5f, "output dot is centred on the bar");
        CheckClose(outRemove.Y, outTopLeft.Y + outSize.Y * 0.5f, "output x is centred on the bar");
        CheckClose(inDot.Y, inTopLeft.Y + inSize.Y * 0.5f, "input dot is centred on the bar");
        CheckClose(inRemove.Y, inTopLeft.Y + inSize.Y * 0.5f, "input x is centred on the bar");

        // The two controls leave room for the label between them and never overlap.
        Check(outDot.X - half > outRemove.X + half, "the two controls of an output bar do not overlap");
        Check(inRemove.X - half > inDot.X + half, "the two controls of an input bar do not overlap");

        Vector2 labelTopLeft, labelSize;
        Check(PortGeometry.TryPortLabelRect(src, outPort, aspect16By9, out labelTopLeft, out labelSize),
              "output label rect");
        Check(labelTopLeft.X >= outRemove.X + half, "the output label starts after its x");
        Check(labelTopLeft.X + labelSize.X <= outDot.X - half, "the output label ends before its dot");
        CheckClose(labelSize.Y, outSize.Y, "the label rect is as tall as its bar");

        Vector2 inLabelTopLeft, inLabelSize;
        Check(PortGeometry.TryPortLabelRect(dst, inPort, aspect16By9, out inLabelTopLeft, out inLabelSize),
              "input label rect");
        Check(inLabelTopLeft.X >= inDot.X + half, "the input label starts after its dot");
        Check(inLabelTopLeft.X + inLabelSize.X <= inRemove.X - half, "the input label ends before its x");

        // One control at each end means both directions reserve exactly the same span.
        CheckClose(labelSize.X, inLabelSize.X, "both directions reserve the same label width");

        // The label sits on the bar's centre line, not on its top edge. Text is positioned by
        // its top edge, so "centred" has to be computed; this is the requirement, and the draw
        // path is the one place it cannot be asserted.
        float lineHeight = VgsConstants.PortBarHeight * 0.6f;
        float labelTop = PortGeometry.CentredTextTop(outTopLeft.Y, outSize.Y, lineHeight);
        CheckClose(labelTop + lineHeight * 0.5f, outTopLeft.Y + outSize.Y * 0.5f,
                   "the label's centre line is the bar's centre line");
        CheckClose(labelTop, outTopLeft.Y + (outSize.Y - lineHeight) * 0.5f,
                   "the label is centred by its measured height");

        // Degenerate inputs: a missing measurement must not pin the text to the top edge, and a
        // text taller than its bar is pinned to the bar's top edge rather than pushed above it —
        // the bar cannot contain such a line, but the label must not start outside its box.
        CheckClose(PortGeometry.CentredTextTop(outTopLeft.Y, outSize.Y, 0f), outTopLeft.Y,
                   "an unmeasured line falls back to the bar's top edge");
        CheckClose(PortGeometry.CentredTextTop(outTopLeft.Y, outSize.Y, outSize.Y * 2f),
                   outTopLeft.Y, "a line taller than its bar is pinned to its top edge");

        // Hit testing: the x answers for its own port and direction, and nothing else does.
        Check(ReferenceEquals(PortGeometry.HitPortRemoveButton(src, outRemove, aspect16By9), outPort),
              "the output x hit-tests to its own port");
        Check(ReferenceEquals(PortGeometry.HitPortRemoveButton(dst, inRemove, aspect16By9), inPort),
              "the input x hit-tests to its own port");
        Check(PortGeometry.HitPortRemoveButton(src, outDot, aspect16By9) == null,
              "the dot is not a remove target");
        Check(PortGeometry.HitPortRemoveButton(src, src.Position + new Vector2(0.01f, 0.01f),
                                               aspect16By9) == null,
              "the card is not a remove target");

        // A port that is not drawn has no bar and therefore no button: the same rule that keeps
        // an unconnected port off the screen keeps it out of the hit test.
        Node idle = MakeTarget(3, "LargeBlockDoor", "Open");
        idle.Position = new Vector2(0.6f, 0.6f);
        graph.AddNode(idle);
        graph.LayoutPorts();

        Port idlePort = idle.FindAction("Open");
        Check(idlePort.Slot < 0, "an unconnected port is not drawn");
        Vector2 unusedCenter;
        Check(!PortGeometry.TryPortRemoveButton(idle, idlePort, aspect16By9, out unusedCenter),
              "an undrawn port has no remove button");
        Check(PortGeometry.HitPortRemoveButton(idle, idle.Position + new Vector2(0.01f,
                                                      VgsConstants.NodeHeight + VgsConstants.PortBarHeight * 0.5f),
                                               aspect16By9) == null,
              "an undrawn port is not a remove target");
    }

    /// <summary>
    /// Removing a port's connections. The graph half of the gesture is what this gate can check:
    /// the button collects every edge the port carries in both fan directions, those edges go
    /// away, and every port that lost its last connection stops being drawn — the other end
    /// included. The toolbar half needs the game and is on the human acceptance list.
    /// </summary>
    private static void PortRemoval()
    {
        var graph = new GridGraph(12);
        Node timer = MakeNode(1, "LargeBlockTimerBlock");
        Node sensor = MakeNode(2, "LargeBlockSensor", SensorToolbar());
        Node door = MakeTarget(3, "LargeBlockDoor", "Open", "Close");

        graph.AddNode(timer);
        graph.AddNode(sensor);
        graph.AddNode(door);

        // Fan-out: one output port drives two actions of the same block.
        Check(graph.TryConnect(1, "Triggered", 3, "Open", new ToolbarCell(0, 0)), "timer -> door Open");
        Check(graph.TryConnect(1, "Triggered", 3, "Close", new ToolbarCell(0, 1)), "timer -> door Close");
        graph.LayoutPorts();

        var collected = new List<Edge>();
        graph.CollectEdgesForPort(1, "Triggered", PortDirection.Output, collected);
        Check(collected.Count == 2, "an output port collects every edge it carries");

        // The direction selects the side: the door has no toolbar, so "Open" is not an output.
        collected.Clear();
        graph.CollectEdgesForPort(3, "Open", PortDirection.Output, collected);
        Check(collected.Count == 0, "the direction selects the side of the edge");

        // Fan-in: two sources drive the same action.
        Check(graph.TryConnect(2, "Detected#true", 3, "Open", new ToolbarCell(0, 2)),
              "sensor -> door Open");
        graph.LayoutPorts();

        collected.Clear();
        graph.CollectEdgesForPort(3, "Open", PortDirection.Input, collected);
        Check(collected.Count == 2, "an input port collects every edge arriving at it");

        // Clearing the input leaves the other input of the same card alone.
        for (int i = 0; i < collected.Count; i++) graph.RemoveEdge(collected[i]);
        graph.LayoutPorts();

        Check(graph.CountEdgesToInput(3, "Open") == 0, "the input's edges are gone");
        Check(door.FindAction("Open").Slot < 0, "the input bar disappears with its last connection");
        Check(door.FindAction("Close").Slot == 0, "the other input of the same card keeps its bar");
        Check(timer.FindEvent("Triggered").Slot == 0,
              "the source keeps its bar while it still drives something");
        Check(sensor.FindEvent("Detected#true").Slot < 0,
              "the source that lost its only edge loses its bar as well");

        // Clearing the last edge of an output port removes that port and the target's bar too.
        collected.Clear();
        graph.CollectEdgesForPort(1, "Triggered", PortDirection.Output, collected);
        Check(collected.Count == 1, "the output has one edge left");
        for (int i = 0; i < collected.Count; i++) graph.RemoveEdge(collected[i]);
        graph.LayoutPorts();

        Check(timer.FindEvent("Triggered").Slot < 0, "the output bar disappears with its last connection");
        Check(door.FindAction("Close").Slot < 0, "the target of the removed edge loses its bar");
        Check(graph.EdgeCount == 0, "all connections are removed");

        collected.Clear();
        graph.CollectEdgesForPort(1, "Triggered", PortDirection.Output, collected);
        Check(collected.Count == 0, "a port with no connections collects nothing");
    }

    private static void Routing()
    {
        var route = new List<Vector4>();

        // Target to the right: leave right, drop at the middle of the line, arrive rightward.
        EdgeRouting.Build(new Vector2(0.1f, 0.1f), new Vector2(0.6f, 0.3f), 0.014f, route);
        Check(route.Count == 3, "rightward route has three segments (got " + route.Count + ")");
        CheckAllAxisAligned(route, "rightward route");
        CheckClose(route[0].X, 0.1f, "route starts at the source dot");
        CheckClose(route[route.Count - 1].Z, 0.6f, "route ends at the target dot (x)");
        CheckClose(route[route.Count - 1].W, 0.3f, "route ends at the target dot (y)");
        Check(route[route.Count - 1].Z > route[route.Count - 1].X,
              "the last segment arrives at the target moving right");

        // The bend is the middle vertical segment. It must sit at the midpoint of the line,
        // not next to the source: that was the defect this replaces.
        CheckClose(route[1].X, (0.1f + 0.6f) * 0.5f, "the bend sits at the middle of the line");
        Check(route[1].X > route[0].X + 0.014f, "the bend is past the source's departure point");

        // A very short line still has to clear the source bar: the midpoint would fall inside
        // the gutter, so the bend is clamped outwards rather than overlapping the source.
        EdgeRouting.Build(new Vector2(0.10f, 0.1f), new Vector2(0.115f, 0.3f), 0.014f, route);
        Check(route[1].X >= 0.10f + 0.014f - 1e-5f,
              "a short line clamps the bend outside the source gutter");

        // Target to the left: leave right, drop to the lane, run back, arrive rightward.
        EdgeRouting.Build(new Vector2(0.7f, 0.2f), new Vector2(0.1f, 0.5f), 0.014f, route);
        Check(route.Count == 5, "leftward route has five segments (got " + route.Count + ")");
        CheckAllAxisAligned(route, "leftward route");
        CheckClose(route[route.Count - 1].Z, 0.1f, "leftward route ends at the target dot (x)");
        Check(route[route.Count - 1].Z > route[route.Count - 1].X,
              "leftward route also arrives moving right");

        // Same height, target to the right: the vertical segment disappears.
        EdgeRouting.Build(new Vector2(0.1f, 0.2f), new Vector2(0.5f, 0.2f), 0.014f, route);
        CheckAllAxisAligned(route, "level route");
        Check(route.Count >= 1, "level route is not empty");
        CheckClose(route[route.Count - 1].Z, 0.5f, "level route reaches the target");

        // A block driving itself: the loop goes around the card instead of through it. The
        // ordinary S would put its middle run at the seam between the two port bars — which are
        // drawn over the lines — and the connection would read as two disconnected stubs
        // (ADR-0036).
        var loopCardTopLeft = new Vector2(0.30f, 0.20f);
        var loopCardSize = new Vector2(VgsConstants.NodeWidth, VgsConstants.NodeHeight);
        EdgeRouting.BuildSelfLoop(new Vector2(0.45412f, 0.245f), new Vector2(0.30588f, 0.263f),
                                  loopCardTopLeft, loopCardSize, 0.014f, route);
        Check(route.Count == 5, "self-loop has five segments (got " + route.Count + ")");
        CheckAllAxisAligned(route, "self-loop");
        CheckClose(route[0].X, 0.45412f, "the self-loop starts at the output dot");
        CheckClose(route[route.Count - 1].Z, 0.30588f, "the self-loop ends at the input dot (x)");
        CheckClose(route[route.Count - 1].W, 0.263f, "the self-loop ends at the input dot (y)");
        Check(route[route.Count - 1].Z > route[route.Count - 1].X,
              "the self-loop arrives at the input dot moving right");

        // The lanes are measured from the card's edges, not from the dots, so the loop cannot
        // fall inside the card no matter how far its dots sit from those edges. The router works
        // in one space, and both the gutter and the dot inset are lengths in that space: the
        // ordinary route is correct because the gutter (0.014) happens to be larger than the
        // inset (≈0.0059), but that is a coincidence of two unrelated constants — a bigger port
        // control or a narrower window would eat it. Anchoring to the card removes the coupling,
        // which is what the assertions below pin: a gutter smaller than the dot inset still
        // leaves both lanes outside the card (ADR-0036).
        Check(route[1].X > loopCardTopLeft.X + loopCardSize.X,
              "the right lane of the loop is outside the card");
        Check(route[3].X < loopCardTopLeft.X, "the left lane of the loop is outside the card");
        Check(route[2].Y < loopCardTopLeft.Y, "the crossing lane of the loop is above the card");

        const float narrowGutter = 0.001f;   // smaller than the dot's own inset from the bar
        EdgeRouting.BuildSelfLoop(new Vector2(0.45412f, 0.245f), new Vector2(0.30588f, 0.263f),
                                  loopCardTopLeft, loopCardSize, narrowGutter, route);
        Check(route.Count == 5, "the loop keeps five segments with a narrow gutter");
        CheckClose(route[1].X, loopCardTopLeft.X + loopCardSize.X + narrowGutter,
                   "the right lane is anchored to the card, not to the dot");
        CheckClose(route[3].X, loopCardTopLeft.X - narrowGutter,
                   "the left lane is anchored to the card, not to the dot");
        CheckClose(route[2].Y, loopCardTopLeft.Y - narrowGutter,
                   "the crossing lane is anchored to the card's top edge");
        Check(route[0].Z > 0.45412f, "the loop still leaves the output dot rightwards");
        Check(route[route.Count - 1].Z > route[route.Count - 1].X,
              "the loop still arrives at the input dot moving right");

        // The same on a real card: both lanes are outside the card's left and right edges and
        // the crossing lane is above its top edge, so no segment is drawn over a port bar.
        var selfGraph = new GridGraph(9);
        Node selfTimer = MakeNode(1, "LargeBlockTimerBlock", "TriggerNow");
        selfTimer.Position = new Vector2(0.30f, 0.20f);
        selfGraph.AddNode(selfTimer);
        selfGraph.TryConnect(1, "Triggered", 1, "TriggerNow", new ToolbarCell(0, 0));
        selfGraph.LayoutPorts();

        Vector2 outDot, inDot, cardTopLeft, cardSize;
        Check(PortGeometry.TryPortDot(selfTimer, selfTimer.FindEvent("Triggered"), 0.5625f, out outDot),
              "the self-connection's output dot exists");
        Check(PortGeometry.TryPortDot(selfTimer, selfTimer.FindAction("TriggerNow"), 0.5625f, out inDot),
              "the self-connection's input dot exists");
        PortGeometry.CardRect(selfTimer, out cardTopLeft, out cardSize);

        // The gutter the editor actually passes, in the router's own units.
        const float realGutter = VgsConstants.LineGutter;
        EdgeRouting.BuildSelfLoop(outDot, inDot, cardTopLeft, cardSize, realGutter, route);
        Check(route.Count == 5, "the loop from the real dots has five segments");
        Check(route[1].X >= cardTopLeft.X + cardSize.X + realGutter - 1e-6f,
              "the real loop's right lane is outside the card");
        Check(route[3].X <= cardTopLeft.X - realGutter + 1e-6f,
              "the real loop's left lane is outside the card");
        Check(route[2].Y <= cardTopLeft.Y - realGutter + 1e-6f,
              "the real loop's crossing lane is above the card");
        Check(route[0].Z >= outDot.X, "the real loop leaves the output dot rightwards");
        Check(route[route.Count - 1].Z >= inDot.X - 1e-6f
              && route[route.Count - 1].Z > route[route.Count - 1].X,
              "the real loop arrives at the input dot moving right");

        // The route buffer is reused, not appended to.
        int before = route.Count;
        EdgeRouting.Build(new Vector2(0.1f, 0.1f), new Vector2(0.2f, 0.2f), 0.014f, route);
        Check(route.Count != before + 3, "the route buffer is cleared on each build");
    }

    private static void Removal()
    {
        var graph = new GridGraph(4);
        Node a = MakeNode(1, "LargeBlockTimerBlock");
        Node b = MakeTarget(2, "LargeBlockSmallGenerator", "OnOff");
        graph.AddNode(a);
        graph.AddNode(b);
        graph.TryConnect(1, "Triggered", 2, "OnOff", new ToolbarCell(0, 0));
        graph.LayoutPorts();
        Check(b.FindAction("OnOff").Slot == 0, "target port is drawn while connected");

        Check(graph.RemoveNode(1), "source node removed");
        Check(graph.EdgeCount == 0, "removing a node removes its edges");
        graph.LayoutPorts();
        Check(b.FindAction("OnOff").Slot < 0, "the target's port is no longer drawn");

        // A port shared by several edges keeps its bar until the last edge is gone.
        Node c = MakeNode(3, "LargeBlockSensor", SensorToolbar());
        graph.AddNode(c);
        graph.TryConnect(3, "Detected#true", 2, "OnOff", new ToolbarCell(0, 0));
        graph.LayoutPorts();
        Check(b.FindAction("OnOff").Slot == 0, "port drawn again after reconnect");
        Check(graph.CountEdgesToInput(2, "OnOff") == 1, "one source");

        // Two connections of the same event, one per fire group, are two edges and one bar.
        Node d = MakeNode(4, "LargeBlockSensor", SensorToolbar());
        graph.AddNode(d);
        graph.TryConnect(4, "Detected#true", 2, "OnOff", new ToolbarCell(0, 0));
        graph.TryConnect(4, "Detected#false", 2, "OnOff", new ToolbarCell(0, 1));
        graph.LayoutPorts();
        Check(graph.CountEdgesFromOutput(4, "Detected#true") == 1, "the yes edge exists");
        Check(graph.CountEdgesFromOutput(4, "Detected#false") == 1, "the no edge exists");
        Check(DrawnOutputs(d) == 2, "the two groups draw two output bars");
        Check(graph.CountEdgesToInput(2, "OnOff") == 3, "three sources on the shared input");
        Check(DrawnInputs(b) == 1, "the shared input still draws one bar");
    }

    /// <summary>
    /// The input stack is placed at "output stack height + one gap", which assumes the output
    /// slots are 0..n-1 with no holes. If LayoutPorts ever left a hole, the input bars would
    /// overlap the output bars. This is the assertion that keeps the two in step.
    /// </summary>
    private static void SlotContiguity()
    {
        var graph = new GridGraph(9);
        Node src = MakeNode(1, "LargeBlockTimerBlock");
        Node dst = MakeTarget(2, "LargeBlockSmallGenerator", "OnOff", "OnOff_On", "OnOff_Off", "Enable");
        graph.AddNode(src);
        graph.AddNode(dst);

        // Three separate inputs on the target, from three separate sources.
        graph.AddNode(MakeNode(3, "LargeBlockSensor", SensorToolbar()));
        graph.AddNode(MakeNode(4, "LargeBlockBatteryBlock"));
        graph.AddNode(MakeNode(5, "LargeBlockCockpit"));

        Check(graph.TryConnect(1, "Triggered", 2, "OnOff", new ToolbarCell(0, 0)), "c1");
        Check(graph.TryConnect(3, "Detected#true", 2, "OnOff_On", new ToolbarCell(0, 0)), "c2");
        Check(graph.TryConnect(4, "Triggered", 2, "OnOff_Off", new ToolbarCell(0, 1)), "c3");
        Check(graph.TryConnect(5, "TargetLocked", 2, "Enable", new ToolbarCell(0, 0)), "c4");
        graph.LayoutPorts();

        Check(DrawnInputs(dst) == 4, "four input bars on the target");

        var seen = new bool[DrawnInputs(dst)];
        for (int i = 0; i < dst.Actions.Count; i++)
        {
            int slot = dst.Actions[i].Slot;
            if (slot < 0) continue;
            Check(slot < seen.Length, "slot within range");
            Check(!seen[slot], "slot " + slot + " is used once");
            if (slot >= 0 && slot < seen.Length) seen[slot] = true;
        }
        for (int i = 0; i < seen.Length; i++) Check(seen[i], "slot " + i + " is contiguous from 0");

        // No two input bars may overlap, which is what contiguity buys.
        for (int i = 0; i < dst.Actions.Count; i++)
        {
            if (dst.Actions[i].Slot < 0) continue;
            for (int j = i + 1; j < dst.Actions.Count; j++)
            {
                if (dst.Actions[j].Slot < 0) continue;

                Vector2 aTop, aSize, bTop, bSize;
                PortGeometry.TryPortBarRect(dst, dst.Actions[i], out aTop, out aSize);
                PortGeometry.TryPortBarRect(dst, dst.Actions[j], out bTop, out bSize);

                bool overlap = aTop.Y < bTop.Y + bSize.Y && bTop.Y < aTop.Y + aSize.Y;
                Check(!overlap, "input bars " + i + " and " + j + " do not overlap");
            }
        }

        // And the input stack as a whole clears the output stack of the same card.
        Node both = MakeNode(6, "LargeBlockSensor", SensorToolbar(), "OnOff");
        graph.AddNode(both);
        Check(graph.TryConnect(1, "Triggered", 6, "OnOff", new ToolbarCell(0, 4)), "sensor gets an input");
        Check(graph.TryConnect(6, "Detected#true", 2, "OnOff", new ToolbarCell(0, 2)), "sensor also sources");
        graph.LayoutPorts();

        Vector2 outTop, outSize, inTop, inSize;
        PortGeometry.TryPortBarRect(both, both.FindEvent("Detected#true"), out outTop, out outSize);
        PortGeometry.TryPortBarRect(both, both.FindAction("OnOff"), out inTop, out inSize);
        Check(inTop.Y >= outTop.Y + outSize.Y, "input stack clears the output stack");

        // The same card's total height matches the bottom of its lowest bar.
        float lowest = inTop.Y + inSize.Y;
        CheckClose(PortGeometry.TotalHeight(both), lowest - both.Position.Y,
                   "total height equals card plus both stacks");

        // And the same identity must hold for a card with inputs only and one with outputs
        // only: TotalHeight is used for culling, so a mismatch hides a card's lower bars.
        Node inOnly = MakeTarget(7, "LargeBlockSmallGenerator", "OnOff");
        inOnly.Position = new Vector2(0.9f, 0.9f);
        graph.AddNode(inOnly);
        Check(graph.TryConnect(1, "Triggered", 7, "OnOff", new ToolbarCell(0, 3)), "input-only target");

        Node outOnly = MakeNode(8, "LargeBlockTimerBlock");
        outOnly.Position = new Vector2(0.9f, 0.2f);
        graph.AddNode(outOnly);
        graph.AddNode(MakeTarget(9, "LargeBlockDoor", "Open"));
        Check(graph.TryConnect(8, "Triggered", 9, "Open", new ToolbarCell(0, 0)), "output-only source");
        graph.LayoutPorts();

        Vector2 onlyTop, onlySize;
        PortGeometry.TryPortBarRect(inOnly, inOnly.FindAction("OnOff"), out onlyTop, out onlySize);
        CheckClose(PortGeometry.TotalHeight(inOnly), onlyTop.Y + onlySize.Y - inOnly.Position.Y,
                   "input-only total height");

        PortGeometry.TryPortBarRect(outOnly, outOnly.FindEvent("Triggered"), out onlyTop, out onlySize);
        CheckClose(PortGeometry.TotalHeight(outOnly), onlyTop.Y + onlySize.Y - outOnly.Position.Y,
                   "output-only total height");
    }

    private static void CheckAllAxisAligned(List<Vector4> route, string what)    {
        for (int i = 0; i < route.Count; i++)
        {
            Vector4 s = route[i];
            bool horizontal = Math.Abs(s.Y - s.W) < 1e-6f;
            bool vertical = Math.Abs(s.X - s.Z) < 1e-6f;
            Check(horizontal || vertical, what + ": segment " + i + " is axis-aligned");
        }
    }

    private static int DrawnOutputs(Node node)
    {
        return PortGeometry.DrawnPortCount(node, PortDirection.Output);
    }

    private static int DrawnInputs(Node node)
    {
        return PortGeometry.DrawnPortCount(node, PortDirection.Input);
    }

    /// <summary>Horizontal half-extent of a port control, the same arithmetic PortGeometry uses.</summary>
    private static float ControlHalfWidth(float aspect)
    {
        return VgsConstants.PortControlRadius * aspect;
    }

    /// <summary>Border-to-centre distance of a port control: the gap plus the half-width.</summary>
    private static float ControlOffset(float aspect)
    {
        return VgsConstants.PortControlMargin * aspect + ControlHalfWidth(aspect);
    }

    /// <summary>
    /// The persisted position format (ADR-0012).
    ///
    /// This is the part of saving that can corrupt a layout without anyone noticing: a value
    /// written in one culture and read in another is still a number, just not the same one, and
    /// the card moves by a little on every reload. The checks below pin the round trip, the
    /// culture independence, and the refusal to accept anything that is not exactly two finite
    /// numbers.
    /// </summary>
    private static void PersistedPositionFormat()
    {
        Vector2 original = new Vector2(0.26f, -1.75f);
        string text = NodeStateFormat.FormatPosition(original);

        Vector2 parsed;
        Check(NodeStateFormat.TryParsePosition(text, out parsed), "a formatted position parses back");
        CheckClose(parsed.X, original.X, "the x of a round trip is unchanged");
        CheckClose(parsed.Y, original.Y, "the y of a round trip is unchanged");

        Check(text.IndexOf(',') < 0, "the format uses no comma, so a decimal comma locale cannot corrupt it");
        Check(text.IndexOf(' ') == text.LastIndexOf(' '), "the format has exactly one separator");

        // Negative and fractional values are the ones a naive parse (int, or a comma split)
        // would get wrong, so they are asserted explicitly.
        Vector2 negative;
        Check(NodeStateFormat.TryParsePosition("-0.125 -3", out negative), "negative values parse");
        CheckClose(negative.X, -0.125f, "a negative fraction survives the round trip");
        CheckClose(negative.Y, -3f, "a negative integer survives the round trip");

        // Rejections. Each of these must fall back to the automatic layout instead of
        // producing a garbage position, so each is a separate assertion.
        Vector2 ignored;
        Check(!NodeStateFormat.TryParsePosition(null, out ignored), "null is not a position");
        Check(!NodeStateFormat.TryParsePosition("", out ignored), "an empty string is not a position");
        Check(!NodeStateFormat.TryParsePosition("   ", out ignored), "whitespace is not a position");
        Check(!NodeStateFormat.TryParsePosition("0.5", out ignored), "a single number is not a position");
        Check(!NodeStateFormat.TryParsePosition("0.5 ", out ignored), "a trailing separator is not a position");
        Check(!NodeStateFormat.TryParsePosition(" 0.5", out ignored), "a leading separator is not a position");
        Check(!NodeStateFormat.TryParsePosition("0.5 0.5 0.5", out ignored), "three parts are not a position");
        Check(!NodeStateFormat.TryParsePosition("a b", out ignored), "non-numeric parts are not a position");
        Check(!NodeStateFormat.TryParsePosition("0,5 1", out ignored), "a decimal comma is not our format");
        Check(!NodeStateFormat.TryParsePosition("NaN 1", out ignored), "NaN is not a position");
        Check(!NodeStateFormat.TryParsePosition("1 Infinity", out ignored), "infinity is not a position");

        // A rejected parse must not leak a half-built value: the caller writes the out
        // parameter straight into the node when it succeeds, so a partial write would show up
        // as a card parked at an arbitrary spot.
        Vector2 rejected = new Vector2(7f, 7f);
        Check(!NodeStateFormat.TryParsePosition("bogus", out rejected), "a bogus value is rejected");
        Check(rejected == Vector2.Zero, "a rejected parse leaves the output at zero, not half-written");
    }

    /// <summary>
    /// "Placed" is a flag, not "position is not zero" (ADR-0012).
    ///
    /// A card parked exactly at the origin is a legitimate layout, and the old test
    /// (<c>Position == Vector2.Zero</c>) moved it on every refresh. These checks pin both
    /// halves: an unplaced node gets a position, and a node placed at the origin does not.
    /// </summary>
    private static void PlacementFlag()
    {
        var graph = new GridGraph(1);

        Node fresh = MakeTarget(1, "LargeBlockConveyor");
        Check(!fresh.Placed, "a new node starts unplaced");

        Node origin = MakeTarget(2, "LargeBlockConveyor");
        origin.Position = Vector2.Zero;
        Check(origin.Placed, "writing (0, 0) marks the node placed");

        // Two unplaced nodes, because the first one legitimately lands at the origin: the
        // automatic layout starts its grid there. The property under test is that the *origin
        // node is not disturbed*, not that unplaced nodes avoid the origin.
        Node fresh2 = MakeTarget(4, "LargeBlockConveyor");

        graph.AddNode(fresh);
        graph.AddNode(fresh2);
        graph.AddNode(origin);
        graph.LayoutUnplaced();

        Check(fresh.Placed, "the automatic layout marks the node it placed");
        Check(fresh2.Placed, "the automatic layout marks every node it placed");
        Check(fresh.Position != fresh2.Position, "two unplaced nodes get different positions");
        Check(origin.Position == Vector2.Zero, "a node placed at the origin is left alone");

        // The same rule after a second pass: a placed node must never be re-seeded, otherwise
        // the card would drift on every refresh.
        Vector2 kept = origin.Position;
        graph.LayoutUnplaced();
        Check(origin.Position == kept, "a placed node is not moved by a later layout pass");

        // A position restored from storage goes through the same property, so it counts as
        // placed without the caller having to remember a second flag.
        Node restored = MakeTarget(3, "LargeBlockConveyor");
        Vector2 parsed;
        Check(NodeStateFormat.TryParsePosition("0 0", out parsed), "the saved origin parses");
        restored.Position = parsed;
        Check(restored.Placed, "a restored position marks the node placed");
        graph.AddNode(restored);
        graph.LayoutUnplaced();
        Check(restored.Position == Vector2.Zero, "a restored position at the origin survives the layout pass");
    }

    /// <summary>
    /// The automatic layout must be a function of the ship, not of the order the game happened
    /// to return blocks in (ADR-0012).
    ///
    /// These positions are never saved, so they are recomputed on every open. If the order
    /// depended on insertion, building or grinding one block would reshuffle every untouched
    /// card between two sessions — which reads to the player as "the editor forgot my layout"
    /// even though nothing was saved to forget.
    /// </summary>
    private static void LayoutIsDeterministic()
    {
        // Same three blocks, added in two different orders, with different grid positions.
        Node a1 = MakeTarget(10, "LargeBlockConveyor");
        a1 = WithGridPosition(a1, new Vector3I(3, 0, 0));
        Node a2 = WithGridPosition(MakeTarget(20, "LargeBlockConveyor"), new Vector3I(1, 0, 0));
        Node a3 = WithGridPosition(MakeTarget(30, "LargeBlockConveyor"), new Vector3I(2, 0, 0));

        var forward = new GridGraph(1);
        forward.AddNode(a1);
        forward.AddNode(a2);
        forward.AddNode(a3);
        forward.LayoutUnplaced();

        Node b1 = WithGridPosition(MakeTarget(30, "LargeBlockConveyor"), new Vector3I(2, 0, 0));
        Node b2 = WithGridPosition(MakeTarget(10, "LargeBlockConveyor"), new Vector3I(3, 0, 0));
        Node b3 = WithGridPosition(MakeTarget(20, "LargeBlockConveyor"), new Vector3I(1, 0, 0));

        var backward = new GridGraph(1);
        backward.AddNode(b1);
        backward.AddNode(b2);
        backward.AddNode(b3);
        backward.LayoutUnplaced();

        // The block at X=1 is first in reading order in both graphs, the one at X=3 is last.
        Check(a2.Position == b3.Position, "the leftmost block gets the same cell in both orders");
        Check(a3.Position == b1.Position, "the middle block gets the same cell in both orders");
        Check(a1.Position == b2.Position, "the rightmost block gets the same cell in both orders");

        // A stable layout also means the same node keeps its cell when a later block is added
        // elsewhere: only the new node is laid out, the old ones are already placed.
        Node late = WithGridPosition(MakeTarget(40, "LargeBlockConveyor"), new Vector3I(9, 0, 0));
        Vector2 before = a1.Position;
        forward.AddNode(late);
        forward.LayoutUnplaced();
        Check(a1.Position == before, "adding a block does not move an already laid out card");
        Check(late.Placed, "the block added later is laid out on its own");

        // ...and the new card must not land on top of an existing one. A layout that restarts
        // its numbering would put every block built while the editor is open at the origin.
        Check(late.Position != a1.Position && late.Position != a2.Position && late.Position != a3.Position,
              "a block added later does not overlap an existing card");

        // Removing a node must not renumber the survivors either: the counter is monotonic.
        Vector2 survivor = a2.Position;
        forward.RemoveNode(a3.EntityId);
        forward.LayoutUnplaced();
        Check(a2.Position == survivor, "removing a node does not move the remaining cards");
    }

    /// <summary>
    /// The construct's reading order: grid first, then position (ADR-0032).
    ///
    /// The graph covers a whole construct, so blocks of different subgrids arrive interleaved in
    /// the terminal system's order. Sorting by position alone would scatter one subgrid's cards
    /// across the canvas, and the frame drawn around them would be a zigzag covering half the
    /// picture. This is the rule that makes a grid frame readable at all, and it is invisible in
    /// the game until someone builds a ship with a subgrid.
    /// </summary>
    private static void ConstructLayoutKeepsGridsTogether()
    {
        const long main = 100L;
        const long sub = 200L;

        // Deliberately interleaved: the two grids alternate, and the subgrid's coordinates are
        // *before* the main grid's, which is the case a position-first sort gets wrong.
        var graph = new GridGraph(main);
        graph.AddNode(OnGrid(1, main, new Vector3I(5, 0, 0)));
        graph.AddNode(OnGrid(2, sub, new Vector3I(1, 0, 0)));
        graph.AddNode(OnGrid(3, main, new Vector3I(6, 0, 0)));
        graph.AddNode(OnGrid(4, sub, new Vector3I(2, 0, 0)));

        graph.LayoutUnplaced();

        Node first = graph.GetNode(1);
        Node second = graph.GetNode(3);
        Node third = graph.GetNode(2);
        Node fourth = graph.GetNode(4);

        // The layout fills six columns per row, so four cards land on one row and the row
        // number says nothing here. What matters is the *order*: a grid's cards must occupy
        // adjacent cells, because that is what makes a frame around them enclose only them.
        //
        // Without the grid as the first key the order would be 1 (x=5, main), 2 (x=1, sub),
        // 3 (x=6, main), 4 (x=2, sub) — sorted by position: 2, 4, 1, 3. The two grids would
        // alternate, and a frame around either would span the whole canvas.
        Check(first.Position.X < second.Position.X,
              "within a grid, cards follow position order");
        Check(third.Position.X < fourth.Position.X,
              "within the second grid, cards follow position order");
        Check(second.Position.X < third.Position.X,
              "one grid's cards are contiguous: the second grid starts after the first ends");

        // A group has no grid of its own, and a block whose grid could not be read has 0. Both
        // are legal: the card is drawn, just without a grid frame (ADR-0032 §5).
        var unread = new Node(9, "LargeBlockConveyor", "unread", new Vector3I(0, 0, 0),
                              ToolbarLayout.None, false, 0);
        Check(unread.GridEntityId == 0, "an unreadable grid is 0, not an error");
        Check(new Node("grp", "Group").GridEntityId == 0, "a group has no grid of its own");

        // Grid 0 sorts before every real grid, so an unreadable block never lands in the middle
        // of a subgrid's block of cards.
        var mixed = new GridGraph(main);
        mixed.AddNode(OnGrid(11, main, new Vector3I(1, 0, 0)));
        mixed.AddNode(unread);
        mixed.LayoutUnplaced();
        Check(unread.Position.X < mixed.GetNode(11).Position.X,
              "a card with no grid sorts before the grids, not inside one");
    }

    /// <summary>
    /// Grid frames (ADR-0032): each box contains exactly the cards of its own grid, and nothing
    /// else.
    ///
    /// A frame is what makes the construct readable — "these cards are one ship part" — so the
    /// failure that matters is a frame that includes a neighbouring grid's card or leaves one of
    /// its own outside. Both are invisible in the game until a ship has a subgrid, and both make
    /// the border a lie.
    /// </summary>
    private static void GridFramesEncloseTheirGrid()
    {
        const long main = 100L;
        const long sub = 200L;

        var graph = new GridGraph(main);

        // Two grids, deliberately far apart so their boxes cannot overlap by accident.
        Node m1 = OnGrid(1, main, new Vector3I(0, 0, 0));
        Node m2 = OnGrid(2, main, new Vector3I(1, 0, 0));
        Node s1 = OnGrid(3, sub, new Vector3I(20, 0, 0));
        Node s2 = OnGrid(4, sub, new Vector3I(21, 0, 0));

        graph.AddNode(m1);
        graph.AddNode(m2);
        graph.AddNode(s1);
        graph.AddNode(s2);
        graph.LayoutUnplaced();

        var frames = new List<GridFrame>(4);
        GridFrames.Layout(graph, frames);

        Check(frames.Count == 2, "one frame per grid, not one per card");

        // ---- each frame contains its own cards ----
        for (int i = 0; i < frames.Count; i++)
        {
            GridFrame frame = frames[i];
            long grid = frame.GridEntityId;

            IList<Node> nodes = graph.Nodes;
            for (int n = 0; n < nodes.Count; n++)
            {
                Node node = nodes[n];
                if (node.GridEntityId != grid) continue;

                Vector2 topLeft, size;
                PortGeometry.TryBodyRect(node, out topLeft, out size);

                Check(frame.Contains(topLeft) && frame.Contains(topLeft + size),
                      "a grid's frame contains every card of that grid");
            }
        }

        // ---- and no card of another grid ----
        GridFrame mainFrame = frames[0];
        GridFrame subFrame = frames[1];

        Vector2 subCard, subSize;
        PortGeometry.TryBodyRect(s1, out subCard, out subSize);
        Check(!mainFrame.Contains(subCard) && !mainFrame.Contains(subCard + subSize),
              "the main grid's frame does not reach into the subgrid's cards");

        Vector2 mainCard, mainSize;
        PortGeometry.TryBodyRect(m1, out mainCard, out mainSize);
        Check(!subFrame.Contains(mainCard) && !subFrame.Contains(mainCard + mainSize),
              "the subgrid's frame does not reach into the main grid's cards");

        // ---- the padding clears a group frame ----
        //
        // A grid frame encloses group frames too. If its padding were not larger than a group's,
        // the two borders would cross and read as a drawing defect rather than as nesting.
        float groupOuter = VgsConstants.GroupFramePadding * 2f + VgsConstants.GroupTitleHeight;
        Check(VgsConstants.GridFramePadding * 2f > groupOuter,
              "a grid frame's padding clears a group frame, so the two borders do not cross");

        // ---- a group and an unreadable block have no frame ----
        var noFrames = new GridGraph(main);
        noFrames.AddNode(new Node("grp", "Group"));
        noFrames.AddNode(new Node(9, "LargeBlockConveyor", "unread", new Vector3I(0, 0, 0),
                                  ToolbarLayout.None, false, 0));
        var empty = new List<GridFrame>(2);
        GridFrames.Layout(noFrames, empty);
        Check(empty.Count == 0, "a group and an unreadable grid produce no frame");

        // ---- the list is reused, not appended to ----
        //
        // The screen owns one buffer for its lifetime (R010). A Layout that appended would grow
        // it every refresh, and the draw pass would paint stale frames over the live ones.
        //
        // The size check is the one that matters: without the clear, a second call would merge the
        // new bounds into the old *and* apply the padding again, so the box would creep outward by
        // a padding per refresh. The count alone cannot see that — it stays at two.
        Vector2 beforeSize = frames[0].Size;
        Vector2 beforeTopLeft = frames[0].TopLeft;
        GridFrames.Layout(graph, frames);
        Check(frames.Count == 2, "laying out twice does not duplicate the frames");
        CheckClose(frames[0].Size.X, beforeSize.X, "laying out twice does not grow a frame's width");
        CheckClose(frames[0].Size.Y, beforeSize.Y, "laying out twice does not grow a frame's height");
        CheckClose(frames[0].TopLeft.X, beforeTopLeft.X,
                   "laying out twice does not creep a frame's origin");

        // ---- a border is drawn in API-pixel space ----
        //
        // Everything below is the arithmetic that decides where a border's edges land. It was
        // wrong three times, each time in a way that looked fine in the reasoning:
        //
        //   1. one thickness for both axes -> 0.56 px tall on a 16:9 window, a translucent line;
        //   2. dividing by the zoom -> 2.9 px at 35%, 0.47 px at 213%;
        //   3. snapping each edge on its own -> the shared corner rounded twice, disagreeing by a
        //      pixel, which is the spur that was reported at every zoom.
        //
        // The third one is why the border is now built from four snapped corners rather than four
        // snapped edges: tiling is a property of the construction, so it cannot be got wrong by a
        // rounding rule.

        // The transform VgsScreenDraw applies: api = layout * scale + offset. On a 16:9 window
        // whose safe area is 4:3, scale is 1920/1680 and the offset is -120/1680.
        const float scale = 1920f / 1680f;
        const float offset = -120f / 1680f;
        const float clientPx = 1920f;

        // Safe area in pixels: one API unit spans `scale` client units.
        float safePx = clientPx / scale;

        // ---- corners land on whole pixels, and stay where they were ----
        //
        // The defect, stated as a check: an unsnapped coordinate lands between pixels. This is
        // what the very first version drew — the right thickness, positioned on a fraction, which
        // a rasteriser turns into two half-covered columns rather than one solid one.
        float rawPx = (0.3511f * scale + offset) * safePx;
        Check(Math.Abs(rawPx - (float)Math.Round(rawPx)) > 0.05f,
              "an unsnapped edge lands between pixels, which is what made it invisible");

        float corner = GridFrames.CornerToApiPixels(scale, offset, clientPx, 0.3511f);
        CheckClose(corner, (float)Math.Round(corner),
                   "a snapped corner lands on a whole API pixel");

        // Snapping must move an edge by at most half a pixel, never somewhere else. This is what
        // catches "round the API value itself": that version also produces a whole pixel, but it
        // puts every border at pixel 0 — a whole screen away.
        Check(Math.Abs(corner - rawPx) <= 0.5f + 1e-3f,
              "snapping moves a corner by at most half a pixel");

        // API units are normalised, not pixels: a corner in API units must be a small fraction,
        // not a pixel count. An early version rounded the API value and collapsed every border
        // into one corner pixel.
        float cornerApi = GridFrames.ApiPixelsToApi(corner, scale, clientPx);
        Check(cornerApi >= 0f && cornerApi <= 1f,
              "an API coordinate stays inside 0..1, which is what the drawing API expects");
        CheckClose(cornerApi * safePx, corner,
                   "converting back to pixels returns the same corner");

        // ---- the four edges tile the outline exactly ----
        //
        // This is the assertion the spur would have failed. Each case is a box whose corners have
        // already been snapped (whole pixels), because that is what the draw path passes.
        float[][] boxes = new float[][]
        {
            new float[] { 100f, 200f, 340f, 300f },     // ordinary
            new float[] { 0f, 0f, 1f, 1f },             // degenerate: one pixel
            new float[] { 50f, 60f, 52f, 62f },         // 2x2: exactly two edges, no interior
            new float[] { 10f, 20f, 300f, 22f },        // 2 px tall: solid fallback
        };

        for (int b = 0; b < boxes.Length; b++)
        {
            float left = boxes[b][0];
            float top = boxes[b][1];
            float right = boxes[b][2];
            float bottom = boxes[b][3];

            var edges = new List<Vector4>(4);
            GridFrames.AppendBorderEdges(edges, left, top, right, bottom, 1f);

            Check(edges.Count > 0, "a border box produces edges");

            // Every edge must lie inside the box: an edge that sticks out is the spur.
            bool inside = true;
            for (int i = 0; i < edges.Count; i++)
            {
                Vector4 e = edges[i];
                if (e.X < left || e.Y < top || e.Z > right || e.W > bottom) inside = false;
            }
            Check(inside, "no border edge extends past the box, at any size");

            if (edges.Count != 4) continue;   // the solid fallback: nothing to tile

            // Top edge spans the full width and the bottom edge does too; the vertical edges fit
            // exactly between them. Any disagreement here is a gap or an overlap at a corner.
            Vector4 topEdge = edges[0];
            Vector4 bottomEdge = edges[1];
            Vector4 leftEdge = edges[2];
            Vector4 rightEdge = edges[3];

            CheckClose(topEdge.X, left, "the top edge starts at the box's left corner");
            CheckClose(topEdge.Z, right, "the top edge ends at the box's right corner");
            CheckClose(bottomEdge.X, left, "the bottom edge starts at the box's left corner");
            CheckClose(bottomEdge.Z, right, "the bottom edge ends at the box's right corner");

            CheckClose(leftEdge.Y, topEdge.W,
                       "the left edge starts exactly where the top edge ends");
            CheckClose(leftEdge.W, bottomEdge.Y,
                       "the left edge ends exactly where the bottom edge starts");
            CheckClose(rightEdge.Y, topEdge.W,
                       "the right edge starts exactly where the top edge ends");
            CheckClose(rightEdge.W, bottomEdge.Y,
                       "the right edge ends exactly where the bottom edge starts");

            CheckClose(leftEdge.X, left, "the left edge sits on the box's left side");
            CheckClose(rightEdge.Z, right, "the right edge sits on the box's right side");
        }

        // ---- the reported defect, stated exactly ----
        //
        // The screenshot showed the horizontal edge ending one pixel past the vertical edge: the
        // bottom edge ran to x=34 while the right edge sat at x=33. That is what "a spur at the
        // corner" means numerically, so this is what the check asserts — exact equality, not
        // closeness. A one-pixel disagreement is the entire visible defect, and CheckClose with
        // its tolerance would call it a pass.
        for (int b = 0; b < boxes.Length; b++)
        {
            var edges = new List<Vector4>(4);
            GridFrames.AppendBorderEdges(edges, boxes[b][0], boxes[b][1], boxes[b][2],
                                         boxes[b][3], 1f);
            if (edges.Count != 4) continue;

            Check(edges[1].Z == edges[3].Z,
                  "the bottom edge ends on the same column as the right edge");
            Check(edges[0].Z == edges[3].Z,
                  "the top edge ends on the same column as the right edge");
            Check(edges[1].X == edges[2].X,
                  "the bottom edge starts on the same column as the left edge");
            Check(edges[2].Y == edges[0].W, "the left edge starts on the top edge's last row");
            Check(edges[2].W == edges[1].Y, "the left edge ends on the bottom edge's first row");
            Check(edges[3].Y == edges[0].W, "the right edge starts on the top edge's last row");
            Check(edges[3].W == edges[1].Y, "the right edge ends on the bottom edge's first row");
        }

        // ---- one pixel tall, at every zoom ----
        //
        // The thickness is now a pixel count, so the zoom cannot affect it: there is no zoom in
        // the expression. The check states that as a property of the output rather than of the
        // formula — every edge is exactly one pixel thick in the box it is derived from.
        float[] zoomSweep = new float[] { 0.35f, 1f, 1.35f, 1.7f, 2.13f, 3f };
        for (int z = 0; z < zoomSweep.Length; z++)
        {
            var edges = new List<Vector4>(4);
            GridFrames.AppendBorderEdges(edges, 10f, 20f, 310f, 220f, 1f);

            bool onePixel = true;
            for (int i = 0; i < edges.Count; i++)
            {
                Vector4 e = edges[i];
                // Horizontal edges are one pixel tall, vertical ones one pixel wide.
                bool horizontal = Math.Abs(e.Y - 20f) < 0.01f || Math.Abs(e.W - 220f) < 0.01f;
                float span = horizontal ? e.W - e.Y : e.Z - e.X;
                if (Math.Abs(span - 1f) > 1e-4f) onePixel = false;
            }
            Check(onePixel, "every border edge is one pixel thick at zoom " + zoomSweep[z]);
        }

        // ---- a box with no interior is filled, not guessed ----
        //
        // A 2x2 box with 1 px edges has no room between them; drawing four rectangles would
        // overlap in a way that reads as a solid block with darker corners on a translucent
        // colour. One rectangle is the honest answer.
        var tiny = new List<Vector4>(4);
        GridFrames.AppendBorderEdges(tiny, 0f, 0f, 2f, 2f, 1f);
        Check(tiny.Count == 1, "a box with no interior is drawn as one filled rectangle");

        // ---- degenerate inputs produce nothing rather than garbage ----
        var noEdges = new List<Vector4>(4);
        GridFrames.AppendBorderEdges(noEdges, 100f, 100f, 100f, 200f, 1f);
        Check(noEdges.Count == 0, "a zero-width box produces no edges");

        GridFrames.AppendBorderEdges(noEdges, 100f, 200f, 50f, 100f, 1f);
        Check(noEdges.Count == 0, "an inverted box produces no edges");

        GridFrames.AppendBorderEdges(noEdges, 10f, 10f, 100f, 100f, 0f);
        Check(noEdges.Count == 0, "a zero thickness produces no edges");
    }

    // ------------------------------------------------------------------ canvas culling (R-015)

    /// <summary>
    /// The canvas rectangle, the predicate that decides what is worth drawing, and the rule that
    /// a border edge outside the canvas is dropped rather than moved onto it.
    ///
    /// <b>Why this is checked here.</b> Both are pure arithmetic and both were wrong in a way no
    /// compile can see. The cull compared an <em>absolute</em> screen coordinate with the canvas
    /// <em>size</em>; because the canvas starts below the header, that cut every card one header
    /// height before it reached the canvas's bottom edge — the card disappeared with a strip of
    /// canvas still below it. And the border clamped its box to the canvas before drawing four
    /// edges, so an edge that was off-screen was painted on the canvas boundary instead of being
    /// dropped: the grid's bottom border lay exactly on the bottom bar's edge.
    /// </summary>
    private static void CanvasCullingAndClippedBorders()
    {
        Vector2 canvasTopLeft, canvasSize;
        CanvasRect.Get(out canvasTopLeft, out canvasSize);

        // ---- the rectangle is the window minus both bars ----
        CheckClose(canvasTopLeft.X, 0f, "the canvas starts at the window's left edge");
        CheckClose(canvasTopLeft.Y, VgsConstants.HeaderHeight,
                   "the canvas starts under the header");
        CheckClose(canvasSize.X, 1f, "the canvas spans the window's full width");
        CheckClose(canvasSize.Y, 1f - VgsConstants.HeaderHeight - VgsConstants.BottomBarHeight,
                   "the canvas ends above the bottom bar");

        float canvasBottom = canvasTopLeft.Y + canvasSize.Y;
        CheckClose(canvasBottom, 1f - VgsConstants.BottomBarHeight,
                   "the canvas's bottom edge is the bottom bar's top edge");

        // A card-shaped rectangle. Its height is irrelevant to the arithmetic; it is here so the
        // checks below read as statements about a card.
        var card = new Vector2(VgsConstants.NodeWidth, 0.2f);

        // ---- R-015: the bottom of the canvas, stated exactly ----
        //
        // The old predicate used the canvas *height* as the bottom boundary — a header height
        // above the real bottom edge. The point below is inside the canvas and inside that dead
        // band: on a 1200 px window a card vanished with about 66 px of canvas still below it,
        // "before it even touched the bottom edge".
        float deadBand = canvasBottom - VgsConstants.HeaderHeight;
        Check(deadBand < canvasBottom, "the dead band the old predicate used is above the bottom");
        Check(CanvasRect.Overlaps(canvasTopLeft, canvasSize, new Vector2(0.5f, deadBand), card),
              "R-015: a card whose top is in the old dead band is still on the canvas");
        Check(CanvasRect.Overlaps(canvasTopLeft, canvasSize,
                                  new Vector2(0.5f, canvasBottom - 0.001f), card),
              "R-015: a card one thousandth above the canvas bottom is still on the canvas");
        Check(!CanvasRect.Overlaps(canvasTopLeft, canvasSize,
                                   new Vector2(0.5f, canvasBottom + 0.001f), card),
              "a card whose top has passed the canvas bottom is not drawn");

        // ---- the top of the canvas is the header's bottom, not the window's top ----
        Check(!CanvasRect.Overlaps(canvasTopLeft, canvasSize,
                                   new Vector2(0.5f, canvasTopLeft.Y - card.Y - 0.001f), card),
              "a card that ends inside the header is not drawn");
        Check(CanvasRect.Overlaps(canvasTopLeft, canvasSize,
                                  new Vector2(0.5f, canvasTopLeft.Y - card.Y + 0.001f), card),
              "a card that pokes below the header is drawn");

        // ---- left and right are the window's edges, because the canvas starts at x = 0 ----
        Check(CanvasRect.Overlaps(canvasTopLeft, canvasSize, new Vector2(-0.01f, 0.3f), card),
              "a card half off the left edge is drawn");
        Check(!CanvasRect.Overlaps(canvasTopLeft, canvasSize,
                                   new Vector2(-card.X - 0.01f, 0.3f), card),
              "a card entirely off the left edge is not drawn");
        Check(!CanvasRect.Overlaps(canvasTopLeft, canvasSize, new Vector2(1.01f, 0.3f), card),
              "a card entirely off the right edge is not drawn");

        // ---- R-015: a border edge outside the canvas is dropped, not moved onto it ----
        //
        // The clip is a canvas 800 x 600 px; the frame's box is 100..400 wide and 50..900 tall, so
        // its bottom edge is 300 px below the canvas and there is no bottom edge to draw. The
        // defect produced one, lying exactly on row 600 — the canvas boundary.
        var edges = new List<Vector4>(4);
        GridFrames.AppendBorderEdgesClipped(edges, 100f, 50f, 400f, 900f,
                                            0f, 0f, 800f, 600f, 1f);
        Check(edges.Count == 3, "a frame whose bottom edge is off the canvas draws three edges");

        bool onCanvasBoundary = false;
        int sidesEndingAtCanvasBottom = 0;
        for (int i = 0; i < edges.Count; i++)
        {
            Vector4 e = edges[i];
            if (e.Y == 599f && e.W == 600f) onCanvasBoundary = true;   // a full 1 px last row
            if (e.W == 600f) sidesEndingAtCanvasBottom++;
        }
        Check(!onCanvasBoundary, "R-015: no bottom edge is painted on the canvas's bottom boundary");
        Check(sidesEndingAtCanvasBottom == 2,
              "the side edges run to the canvas bottom instead of stopping at a drawn edge");

        // ---- a frame entirely inside the canvas is unchanged by clipping ----
        var plain = new List<Vector4>(4);
        GridFrames.AppendBorderEdges(plain, 100f, 50f, 400f, 500f, 1f);
        var clipped = new List<Vector4>(4);
        GridFrames.AppendBorderEdgesClipped(clipped, 100f, 50f, 400f, 500f,
                                            0f, 0f, 800f, 600f, 1f);
        Check(plain.Count == 4 && clipped.Count == 4,
              "a frame inside the canvas draws four edges either way");

        bool identical = plain.Count == clipped.Count;
        for (int i = 0; identical && i < plain.Count; i++)
        {
            if (plain[i].X != clipped[i].X || plain[i].Y != clipped[i].Y
                || plain[i].Z != clipped[i].Z || plain[i].W != clipped[i].W)
            {
                identical = false;
            }
        }
        Check(identical, "clipping changes nothing for a frame that is entirely on the canvas");

        // ---- the top edge is dropped the same way, and the sides reach the canvas top ----
        edges.Clear();
        GridFrames.AppendBorderEdgesClipped(edges, 100f, -50f, 400f, 500f,
                                            0f, 0f, 800f, 600f, 1f);
        Check(edges.Count == 3, "a frame whose top edge is above the canvas draws three edges");

        bool topOnBoundary = false;
        int sidesStartingAtCanvasTop = 0;
        for (int i = 0; i < edges.Count; i++)
        {
            Vector4 e = edges[i];
            if (e.Y == 0f && e.W == 1f) topOnBoundary = true;          // a full 1 px first row
            if (e.Y == 0f) sidesStartingAtCanvasTop++;
        }
        Check(!topOnBoundary, "R-015: no top edge is painted on the canvas's top boundary");
        Check(sidesStartingAtCanvasTop == 2,
              "the side edges reach the canvas top instead of stopping a pixel below it");

        // ---- nothing at all is drawn when nothing is on the canvas ----
        edges.Clear();
        GridFrames.AppendBorderEdgesClipped(edges, -1000f, -1000f, 1000f, 1000f,
                                            0f, 0f, 800f, 600f, 1f);
        Check(edges.Count == 0, "a frame that surrounds the canvas draws no edge on it");

        edges.Clear();
        GridFrames.AppendBorderEdgesClipped(edges, 0f, 700f, 800f, 900f,
                                            0f, 0f, 800f, 600f, 1f);
        Check(edges.Count == 0, "a frame entirely below the canvas draws nothing");

        // ---- an edge that starts off the canvas is still cut along its own direction ----
        //
        // The frame is wider than the canvas, so both its side edges are off-screen and are
        // dropped. Its horizontal edges are on screen — only their middle — and are drawn from
        // the canvas's left edge to its right edge, because that part of them is genuinely
        // visible. Dropping those too would leave a frame that crosses the canvas invisible.
        edges.Clear();
        GridFrames.AppendBorderEdgesClipped(edges, -100f, 50f, 900f, 500f,
                                            0f, 0f, 800f, 600f, 1f);
        Check(edges.Count == 2, "a frame wider than the canvas draws only its two horizontal edges");
        Check(edges[0].X == 0f && edges[0].Z == 800f,
              "a horizontal edge that starts off the canvas is cut at the canvas's left edge");
        Check(edges[1].X == 0f && edges[1].Z == 800f,
              "a horizontal edge that ends off the canvas is cut at the canvas's right edge");

        // ---- a box with no interior is still one rectangle ----
        edges.Clear();
        GridFrames.AppendBorderEdgesClipped(edges, 10f, 10f, 12f, 12f,
                                            0f, 0f, 800f, 600f, 1f);
        Check(edges.Count == 1, "a frame with no interior is one rectangle, clipped or not");

        edges.Clear();
        GridFrames.AppendBorderEdgesClipped(edges, 10f, 10f, 100f, 100f,
                                            0f, 0f, 800f, 600f, 0f);
        Check(edges.Count == 0, "a zero thickness produces no edges when clipped");
    }

    private static Node WithGridPosition(Node node, Vector3I position)
    {
        // Node.GridPosition is readonly and set in the constructor, so rebuild the node.
        return new Node(node.EntityId, node.SubtypeId, node.DisplayName, position, node.Toolbar);
    }

    /// <summary>A card on a named grid, for the construct-layout checks (ADR-0032).</summary>
    private static Node OnGrid(long id, long gridId, Vector3I position)
    {
        return new Node(id, "LargeBlockConveyor", "block" + id, position, ToolbarLayout.None,
                        false, gridId);
    }

    // ------------------------------------------------------------------ block groups (ADR-0015)

    /// <summary>A member whose actions are enabled on it — the ordinary case.</summary>
    private static GroupMemberActions EnabledMember(string typeKey, bool functional, params string[] actions)
    {
        var ids = new List<string>(actions);
        return new GroupMemberActions(typeKey, functional, ids, new List<string>(ids));
    }

    /// <summary>
    /// A member of the given type whose action list is <paramref name="actions"/> but which has
    /// none of them enabled — a switched-off block.
    /// </summary>
    private static GroupMemberActions DisabledMember(string typeKey, params string[] actions)
    {
        return new GroupMemberActions(typeKey, true, new List<string>(actions), new List<string>(0));
    }

    /// <summary>
    /// The rule the game itself uses to decide which actions a group accepts
    /// (<c>docs/01-research/api-block-groups.md</c> §3). Every assertion here is a case the
    /// game distinguishes, and each one decides what the editor offers in the action menu:
    /// offering an action the game would not fire is a connection that silently does nothing.
    /// </summary>
    private static void GroupActionRule()
    {
        var ids = new List<string>();

        // One type: that type's actions, in its order, without duplicates.
        BlockPortCatalog.ComputeGroupActionIds(
            new List<GroupMemberActions>
            {
                EnabledMember("MyTimerBlock", true, "TriggerNow", "Start", "Stop"),
                EnabledMember("MyTimerBlock", true, "TriggerNow", "Start", "Stop")
            }, ids);
        Check(ids.Count == 3 && ids[0] == "TriggerNow" && ids[2] == "Stop",
              "a single-type group takes its type's actions in order");

        // Two types, all functional: the intersection, in the first member's order.
        BlockPortCatalog.ComputeGroupActionIds(
            new List<GroupMemberActions>
            {
                EnabledMember("MyDoor", true, "Open", "Close", "OnOff"),
                EnabledMember("MyAirVent", true, "OnOff", "Depressurize")
            }, ids);
        Check(ids.Count == 1 && ids[0] == "OnOff",
              "a mixed group keeps only the actions every member has");

        // Intersection order follows the first member, not the second.
        BlockPortCatalog.ComputeGroupActionIds(
            new List<GroupMemberActions>
            {
                EnabledMember("MyDoor", true, "Close", "Open", "OnOff"),
                EnabledMember("MyAirVent", true, "OnOff", "Open")
            }, ids);
        Check(ids.Count == 2 && ids[0] == "Open" && ids[1] == "OnOff",
              "the intersection keeps the first member's order");

        // A mixed group with one non-functional member has no actions at all. This is the
        // game's rule (`isinst MyFunctionalBlock` per member), not a simplification: a group
        // with one conveyor tube in it offers nothing in the vanilla toolbar either.
        BlockPortCatalog.ComputeGroupActionIds(
            new List<GroupMemberActions>
            {
                EnabledMember("MyDoor", true, "Open", "Close"),
                EnabledMember("MyConveyorTube", false, "Open", "Close")
            }, ids);
        Check(ids.Count == 0, "a mixed group with a non-functional member has no actions");

        // ...but the same non-functional block in a single-type group is fine, because the
        // game takes the type's actions without the functional test in that branch.
        BlockPortCatalog.ComputeGroupActionIds(
            new List<GroupMemberActions>
            {
                EnabledMember("MyConveyorTube", false, "OnOff"),
                EnabledMember("MyConveyorTube", false, "OnOff")
            }, ids);
        Check(ids.Count == 1 && ids[0] == "OnOff",
              "a single-type group of non-functional blocks keeps its type's actions");

        // The enabled filter, single type: "any member" (the game's own asymmetry).
        BlockPortCatalog.ComputeGroupActionIds(
            new List<GroupMemberActions>
            {
                DisabledMember("MyDoor", "Open", "Close"),
                EnabledMember("MyDoor", true, "Open", "Close")
            }, ids);
        Check(ids.Count == 2 && ids[0] == "Open" && ids[1] == "Close",
              "one type: an action enabled on any member survives");

        // ...and when no member enables it, it is not offered.
        BlockPortCatalog.ComputeGroupActionIds(
            new List<GroupMemberActions>
            {
                DisabledMember("MyDoor", "Open", "Close"),
                DisabledMember("MyDoor", "Open", "Close")
            }, ids);
        Check(ids.Count == 0, "one type: an action no member enables is not offered");

        // The enabled filter, mixed types: "every member". A switched-off block removes the
        // action here, which is the opposite of the single-type branch.
        BlockPortCatalog.ComputeGroupActionIds(
            new List<GroupMemberActions>
            {
                EnabledMember("MyDoor", true, "Open", "OnOff"),
                new GroupMemberActions("MyAirVent", true,
                    new List<string> { "Open", "OnOff" }, new List<string> { "OnOff" })
            }, ids);
        Check(ids.Count == 1 && ids[0] == "OnOff",
              "mixed types: an action disabled on one member is dropped");

        // No members, no actions — and no exception. A null member must not throw either: the
        // caller builds this list from a world read and cannot guarantee every entry.
        BlockPortCatalog.ComputeGroupActionIds(new List<GroupMemberActions>(), ids);
        Check(ids.Count == 0, "an empty group has no actions");
        BlockPortCatalog.ComputeGroupActionIds(null, ids);
        Check(ids.Count == 0, "a null member list has no actions");
        BlockPortCatalog.ComputeGroupActionIds(
            new List<GroupMemberActions> { null, EnabledMember("MyDoor", true, "Open") }, ids);
        Check(ids.Count == 1 && ids[0] == "Open", "a null member is skipped, not fatal");
    }

    /// <summary>A member whose actions carry the game's captions, for the caption checks.</summary>
    private static GroupMemberActions NamedMember(string typeKey, params string[] actions)
    {
        var ids = new List<string>(actions);
        var names = new List<string>(ids.Count);
        for (int i = 0; i < ids.Count; i++) names.Add("caption:" + ids[i]);
        return new GroupMemberActions(typeKey, true, ids, new List<string>(ids), names);
    }

    /// <summary>
    /// An action port is captioned with the game's own localized action name, and its key stays
    /// the id (ADR-0037).
    ///
    /// The id is a save-data key — "OnOff_On", "IncreaseTextPaddingSlider" — and the editor used
    /// to show a Humanized version of it ("On Off_ On"). The game has a label for every action
    /// already, and the vanilla toolbar shows it, so the caption is read from
    /// <c>ITerminalAction.Name</c> in <c>Vgs.Game</c> and passed in parallel with the id. The
    /// assertions below pin the three things that can silently regress: the caption is the name,
    /// the key is the id, and a missing or short name list degrades to the humanized id instead of
    /// dropping the port.
    /// </summary>
    private static void ActionCaptions()
    {
        var node = new Node(1, "TimerBlockLarge", "timer", Vector3I.Zero, ToolbarLayout.None);

        BlockPortCatalog.FillActions(
            node,
            new List<string> { "OnOff", "OnOff_On", "Silent" },
            new List<string> { "Toggle block On/Off", "Toggle block On", "Silent On/Off" });

        Check(node.Actions.Count == 3, "three actions make three ports");
        Check(node.Actions[0].DisplayName == "Toggle block On/Off",
              "the caption is the game's action name, not the id");
        Check(node.Actions[2].DisplayName == "Silent On/Off", "every action keeps its own caption");
        Check(node.Actions[0].Key == "OnOff", "the key stays the id whatever the caption says");

        BlockPortCatalog.FillActions(node, new List<string> { "OnOff", "TriggerNow" }, null);
        Check(node.Actions[0].DisplayName == "On Off", "with no names the id is humanized");
        Check(node.Actions[1].DisplayName == "Trigger Now", "the fallback humanizes every id");

        BlockPortCatalog.FillActions(node, new List<string> { "OnOff", "TriggerNow" },
                                     new List<string> { "Toggle block On/Off" });
        Check(node.Actions[0].DisplayName == "Toggle block On/Off",
              "a name is used when the list has one");
        Check(node.Actions[1].DisplayName == "Trigger Now",
              "a short name list falls back per action, it does not drop the port");

        var names = new List<string>();
        var ids = new List<string>();
        BlockPortCatalog.ComputeGroupActionIds(
            new List<GroupMemberActions>
            {
                NamedMember("MyTimerBlock", "TriggerNow", "Start", "Stop"),
                NamedMember("MyTimerBlock", "TriggerNow", "Start", "Stop")
            }, ids, names);
        Check(names.Count == ids.Count, "a group gets one caption per surviving action");
        Check(names[0] == "caption:TriggerNow", "a group caption comes from the member's name list");

        BlockPortCatalog.ComputeGroupActionIds(
            new List<GroupMemberActions>
            {
                NamedMember("MyDoor", "Open", "OnOff"),
                EnabledMember("MyAirVent", true, "OnOff")
            }, ids, names);
        Check(names.Count == ids.Count && names[0] == "caption:OnOff",
              "the intersection is captioned from the first member's names");

        // A member without names must not leave a hole in the parallel list: the catalog then
        // humanizes that one caption and the ids and names stay aligned.
        BlockPortCatalog.ComputeGroupActionIds(
            new List<GroupMemberActions> { EnabledMember("MyDoor", true, "Open", "Close") },
            ids, names);
        Check(names.Count == ids.Count, "a nameless member still yields one caption per action");
        Check(names[0] == null, "a nameless member yields an empty caption, not a shifted list");
    }

    /// <summary>
    /// An action id names the control the game built it from (ADR-0039): the control's own id, or
    /// its id with <c>_On</c>/<c>_Off</c>, or prefixed with <c>Increase</c>/<c>Decrease</c>.
    ///
    /// The editor drops an action whose control the game hides for the block, so this mapping is
    /// the difference between hiding a leaked action and hiding a real one:
    /// <c>IncreaseFontSize</c> must belong to the hidden <c>FontSize</c> slider, while
    /// <c>IncreaseSafetyDetach</c> must belong to the hinge's own <c>SafetyDetach</c>.
    /// </summary>
    private static void ActionControlMapping()
    {
        Check(BlockPortCatalog.ActionBelongsToControl("OnOff", "OnOff"), "a plain action belongs to its control");
        Check(BlockPortCatalog.ActionBelongsToControl("OnOff_On", "OnOff"), "_On belongs to its control");
        Check(BlockPortCatalog.ActionBelongsToControl("OnOff_Off", "OnOff"), "_Off belongs to its control");
        Check(BlockPortCatalog.ActionBelongsToControl("IncreaseFontSize", "FontSize"),
              "Increase+control belongs to that control");
        Check(BlockPortCatalog.ActionBelongsToControl("DecreaseFontSize", "FontSize"),
              "Decrease+control belongs to that control");
        Check(BlockPortCatalog.ActionBelongsToControl("IncreaseTextPaddingSlider", "TextPaddingSlider"),
              "a slider action belongs to the slider control");
        Check(BlockPortCatalog.ActionBelongsToControl("PreserveAspectRatio", "PreserveAspectRatio"),
              "a checkbox action belongs to its own control");

        Check(!BlockPortCatalog.ActionBelongsToControl("OnOff_On", "On"),
              "a suffix is not a prefix of another control's id");
        Check(!BlockPortCatalog.ActionBelongsToControl("IncreaseSafetyDetach", "FontSize"),
              "the hinge's own action does not belong to the text panel's slider");
        Check(!BlockPortCatalog.ActionBelongsToControl("Reverse", "ReverseHandle"),
              "a plain action is not a prefix of a longer control id");
        Check(!BlockPortCatalog.ActionBelongsToControl("Increase", "FontSize"),
              "the prefix with no control id is not an action of that control");

        var hidden = new List<string> { "FontSize", "TextPaddingSlider" };
        Check(BlockPortCatalog.IsActionHiddenBy("IncreaseFontSize", hidden),
              "an action of a hidden control is hidden");
        Check(BlockPortCatalog.IsActionHiddenBy("DecreaseTextPaddingSlider", hidden),
              "every action of a hidden control is hidden");
        Check(!BlockPortCatalog.IsActionHiddenBy("IncreaseTorque", hidden),
              "an action of a visible control is not hidden");
        Check(!BlockPortCatalog.IsActionHiddenBy("IncreaseFontSize", new List<string>()),
              "with no hidden controls nothing is hidden");
        Check(!BlockPortCatalog.IsActionHiddenBy("IncreaseFontSize", null),
              "a missing control list hides nothing");
    }

    /// <summary>
    /// The group node and its two-sided visibility rule: the group exists while at least one
    /// member is drawn, and its action list is computed from the whole group — including the
    /// members that are hidden (ADR-0015).
    /// </summary>
    private static void GroupNodes()
    {
        var graph = new GridGraph(7);
        Node timer = MakeNode(1, "LargeBlockTimerBlock");
        Node door = MakeTarget(2, "LargeBlockDoor", "Open", "Close", "OnOff");
        graph.AddNode(timer);
        graph.AddNode(door);

        var members = new List<long> { 1, 2 };
        var actions = new List<GroupMemberActions>
        {
            EnabledMember("MyDoor", true, "Open", "Close", "OnOff"),
            EnabledMember("MyDoor", true, "Open", "Close", "OnOff")
        };

        Node group = graph.SyncGroup("Doors", members, actions);
        Check(group != null, "a group with a drawn member becomes a node");
        Check(group.IsGroup, "the node knows it is a group");
        Check(group.EntityId == 0, "a group has no entity id");
        Check(group.GroupName == "Doors", "the group's identity is its name");
        Check(group.Members.Count == 2, "the group remembers its drawn members");
        Check(group.Actions.Count == 3, "the group exposes the actions its members share");
        Check(group.Events.Count == 0, "a group has no output ports");
        Check(!group.CanDriveBlocks, "a group cannot drive other blocks");
        Check(group.Toolbar == ToolbarLayout.None, "a group has no toolbar");
        Check(graph.GroupCount == 1 && graph.GetGroup("Doors") == group, "the graph can find the group by name");
        Check(graph.ContainsNode(group), "the graph recognises the group as live");

        // Hidden members are not drawn but still count for the action list: the caller passes
        // the whole group's actions and only the drawn members.
        Node group2 = graph.SyncGroup("Doors", new List<long> { 2 },
                                      new List<GroupMemberActions>
                                      {
                                          EnabledMember("MyDoor", true, "Open", "Close", "OnOff"),
                                          EnabledMember("MyDoor", true, "Open", "Close", "OnOff")
                                      });
        Check(ReferenceEquals(group, group2), "re-syncing keeps the same node object");
        Check(group.Members.Count == 1, "only the drawn members are framed");
        Check(group.Actions.Count == 3, "the action list still comes from the whole group");

        // No drawn member: the group leaves the graph. This is how "Show in VGS" aggregates.
        Check(graph.SyncGroup("Doors", new List<long>(), actions) == null,
              "a group with no drawn member has no node");
        Check(graph.GroupCount == 0, "the group is gone from the graph");
        Check(!graph.ContainsNode(group), "the graph no longer recognises the group");

        // A renamed group is a different group: the game addresses it by name.
        graph.SyncGroup("Doors", members, actions);
        Check(graph.GetGroup("Doors") != null, "the group is back under its name");
        Check(graph.GetGroup("Other") == null, "a different name is a different group");

        // When the composition changes so that an action drops out of the group's list, the
        // edges that ended on that port go with it: a port the node no longer has would draw
        // nothing at the target and leave a dot with no line at the source.
        graph.SyncGroup("Doors", members,
                        new List<GroupMemberActions>
                        {
                            EnabledMember("MyDoor", true, "Open", "Close", "OnOff")
                        });
        Check(graph.GetGroup("Doors").FindAction("OnOff") != null, "the group offers OnOff");
        Check(graph.TryConnectToGroup(1, "Triggered", "Doors", "OnOff", new ToolbarCell(0, 4)),
              "a connection to the new port");
        graph.LayoutPorts();
        Check(graph.GetGroup("Doors").FindAction("OnOff").Slot == 0, "the OnOff port is drawn");
        Check(graph.EdgeCount == 1, "the connection to the new port exists");

        graph.SyncGroup("Doors", members,
                        new List<GroupMemberActions>
                        {
                            EnabledMember("MyDoor", true, "Open", "Close"),
                            EnabledMember("MyDoor", true, "Open", "Close")
                        });
        Check(graph.GetGroup("Doors").FindAction("OnOff") == null, "OnOff is no longer offered");
        Check(graph.EdgeCount == 0, "the edge to the vanished port is gone");
        Check(graph.GetGroup("Doors").FindAction("Open").Slot < 0,
              "the surviving port has no edge, so it draws no bar");
    }

    /// <summary>
    /// Connections to a group: legal as a target, illegal as a source, and they keep the
    /// graph's cell and port invariants (ADR-0015).
    /// </summary>
    private static void GroupEdges()
    {
        var graph = new GridGraph(8);
        Node timer = MakeNode(1, "LargeBlockTimerBlock");
        Node sensor = MakeNode(3, "LargeBlockSensor", SensorToolbar());
        Node door = MakeTarget(2, "LargeBlockDoor", "Open", "Close", "OnOff");
        graph.AddNode(timer);
        graph.AddNode(sensor);
        graph.AddNode(door);

        var members = new List<long> { 2 };
        var actions = new List<GroupMemberActions> { EnabledMember("MyDoor", true, "Open", "Close") };
        Node group = graph.SyncGroup("Doors", members, actions);
        Check(group.Actions.Count == 2, "the group exposes the two door actions");

        Check(graph.TryConnectToGroup(1, "Triggered", "Doors", "Open", new ToolbarCell(0, 0)),
              "a block connects to a group");
        graph.LayoutPorts();
        Check(graph.EdgeCount == 1, "the edge exists");
        Check(group.FindAction("Open").Slot == 0, "the group's port gets a bar");
        Check(timer.FindEvent("Triggered").Slot == 0, "the source's port gets a bar");
        Check(graph.CountEdgesToInput(group, "Open") == 1, "the group's fan-in counts its own edges");
        Check(graph.CountEdgesToInput(2, "Open") == 0, "a group edge is not counted as a block edge");
        Check(graph.HasEdgeToGroup(1, "Triggered", "Doors", "Open"), "the group edge is findable");
        Check(!graph.HasEdge(1, "Triggered", 2, "Open"), "a group edge is not a block edge");
        Check(graph.GetTargetNode(graph.Edges[0]) == group, "the edge resolves to the group node");

        // An action the group does not have is refused, exactly as for a block.
        Check(!graph.TryConnectToGroup(1, "Triggered", "Doors", "Depressurize", new ToolbarCell(0, 1)),
              "an action the group does not offer is refused");
        Check(!graph.TryConnectToGroup(1, "Triggered", "Missing", "Open", new ToolbarCell(0, 1)),
              "an unknown group is refused");
        Check(!graph.TryConnectToGroup(1, "Triggered", "Doors", "Open", new ToolbarCell(0, 0)),
              "the same cell cannot hold two connections");

        // A second source on the same group port is normal fan-in, as for a block.
        Check(graph.TryConnectToGroup(3, "Detected#true", "Doors", "Open", new ToolbarCell(0, 0)),
              "a second source joins the same group port");
        graph.LayoutPorts();
        Check(graph.CountEdgesToInput(group, "Open") == 2, "two sources on the group's port");
        Check(group.FindAction("Open").Slot == 0, "the shared group port still draws one bar");

        // A group has no outputs, so nothing can start from it.
        Check(!graph.TryConnect(group.EntityId, "Triggered", 2, "Open", new ToolbarCell(0, 0)),
              "a group cannot be the source of an edge");

        // Removing the group drops its edges and leaves the block edges alone.
        Check(graph.TryConnect(1, "Triggered", 2, "Open", new ToolbarCell(0, 1)), "a block edge too");
        Check(graph.RemoveGroup("Doors"), "the group is removed");
        Check(graph.EdgeCount == 1, "only the block edge survives");
        Check(!graph.ContainsGroup("Doors"), "the group is gone");
        Check(!graph.RemoveGroup("Doors"), "removing it twice is a no-op");

        // ...and the collect-for-port overload works for both kinds of node.
        graph.SyncGroup("Doors", members, actions);
        graph.TryConnectToGroup(1, "Triggered", "Doors", "Open", new ToolbarCell(0, 2));
        var buffer = new List<Edge>();
        graph.CollectEdgesForPort(graph.GetGroup("Doors"), "Open", PortDirection.Input, buffer);
        Check(buffer.Count == 1, "the group's port collects its own edge");
        buffer.Clear();
        graph.CollectEdgesForPort(1, "Triggered", PortDirection.Output, buffer);
        Check(buffer.Count == 2, "the source's port collects the block edge and the group edge");

        // The same group connection repeated on another toolbar page is a second edge and one
        // line: the repeat is legal in the game (a different page), but drawing it twice would
        // only hide that a repeat happened.
        Check(graph.TryConnectToGroup(1, "Triggered", "Doors", "Open", new ToolbarCell(0, 3)),
              "the same group connection on another page is a separate edge");
        graph.LayoutPorts();
        Check(graph.CountEdgesFromOutput(1, "Triggered") == 3, "three edges leave the source port");
        Check(graph.Lines.Count == 2, "but only two lines are drawn — one per distinct pair");

        // Losing the last connection takes the group's bar away, like any other port.
        var groupBuffer = new List<Edge>();
        graph.CollectEdgesForPort(graph.GetGroup("Doors"), "Open", PortDirection.Input, groupBuffer);
        Check(groupBuffer.Count == 2, "both group edges are collected");
        for (int i = 0; i < groupBuffer.Count; i++) graph.RemoveEdge(groupBuffer[i]);
        graph.LayoutPorts();
        Check(graph.GetGroup("Doors").FindAction("Open").Slot < 0,
              "the group's port is no longer drawn once its last edge is gone");
    }

    /// <summary>
    /// The group frame: a derived box around the members' cards and their port stacks, with
    /// the title strip above it. The draw pass and the hit test both read it, so it is asserted
    /// here rather than discovered in game.
    /// </summary>
    private static void GroupGeometry()
    {
        var graph = new GridGraph(11);
        Node a = MakeTarget(1, "LargeBlockConveyor");
        Node b = MakeTarget(2, "LargeBlockConveyor");
        Node source = MakeNode(3, "LargeBlockTimerBlock");
        graph.AddNode(a);
        graph.AddNode(b);
        graph.AddNode(source);

        a.Position = new Vector2(0.30f, 0.20f);
        b.Position = new Vector2(0.10f, 0.40f);

        var members = new List<long> { 1, 2 };
        var actions = new List<GroupMemberActions> { EnabledMember("MyConveyor", false, "OnOff") };
        Node group = graph.SyncGroup("Belt", members, actions);
        Check(graph.TryConnectToGroup(3, "Triggered", "Belt", "OnOff", new ToolbarCell(0, 0)),
              "a source connects to the group");
        graph.LayoutPorts();
        PortGeometry.LayoutGroupFrames(graph);

        Vector2 frameTopLeft, frameSize;
        Check(PortGeometry.TryBodyRect(group, out frameTopLeft, out frameSize), "the group has a frame");

        float pad = VgsConstants.GroupFramePadding;
        float title = VgsConstants.GroupTitleHeight;

        // The box spans both cards horizontally and vertically, plus the padding and the strip.
        CheckClose(frameTopLeft.X, 0.10f - pad, "the frame starts at the leftmost card minus padding");
        CheckClose(frameTopLeft.Y, 0.20f - pad - title, "the frame starts above the topmost card, by the title strip");
        CheckClose(frameSize.X, (0.30f + VgsConstants.NodeWidth) - 0.10f + pad * 2f,
                   "the frame spans the cards' width plus padding");
        Check(frameSize.Y > VgsConstants.NodeHeight * 2f, "the frame is taller than two cards");

        // The frame contains the member's port stack: a member with a connected port is taller
        // than its card, and a frame that ignored that would cut through its own ports.
        CheckClose(frameTopLeft.Y + frameSize.Y,
                   0.40f + PortGeometry.TotalHeight(b) + pad,
                   "the frame's bottom edge clears the lowest member's port stack");

        // The title strip is the group's click target and sits at the frame's top.
        Vector2 titleTopLeft, titleSize;
        Check(PortGeometry.TryGroupTitleRect(group, out titleTopLeft, out titleSize), "the group has a title strip");
        CheckClose(titleTopLeft.Y, frameTopLeft.Y, "the title strip is at the frame's top");
        CheckClose(titleSize.Y, title, "the title strip has the title height");

        // Hit-testing: the strip and the port bars are the group; the interior is not.
        Check(PortGeometry.HitNode(group, titleTopLeft + new Vector2(titleSize.X * 0.5f, titleSize.Y * 0.5f)),
              "the title strip hits the group");
        Check(!PortGeometry.HitNode(group, frameTopLeft + new Vector2(frameSize.X * 0.5f, frameSize.Y * 0.5f)),
              "the frame's interior does not hit the group — the members are there");
        Check(!PortGeometry.HitNode(group, new Vector2(0.5f, 0.5f)), "a point outside the frame misses");

        Vector2 barTopLeft, barSize;
        Check(PortGeometry.TryPortBarRect(group, group.FindAction("OnOff"), out barTopLeft, out barSize),
              "the group's port bar has a rectangle");
        CheckClose(barTopLeft.Y, frameTopLeft.Y + frameSize.Y,
                   "the group's port bar hangs from the frame's bottom edge");
        CheckClose(barSize.X, frameSize.X, "the group's port bar is as wide as the frame");
        Check(PortGeometry.HitNode(group, barTopLeft + new Vector2(barSize.X * 0.5f, barSize.Y * 0.5f)),
              "the port bar hits the group");

        // A group with no members has no frame, so nothing is drawn at the origin.
        Node orphan = new Node("Empty", "Empty");
        Vector2 ignoredTopLeft, ignoredSize;
        Check(!PortGeometry.TryBodyRect(orphan, out ignoredTopLeft, out ignoredSize),
              "a group with no frame reports no body rectangle");
    }

    /// <summary>
    /// Dragging a group by its title strip (ADR-0016): the whole cluster translates, the
    /// members keep their positions relative to each other, the frame follows, and nothing
    /// outside the group moves.
    ///
    /// These are the properties the player judges by eye — "the group moved together" — and
    /// they are decidable without the game, which is why they are asserted here instead of
    /// being discovered on a ship.
    /// </summary>
    private static void GroupDrag()
    {
        var graph = new GridGraph(12);
        Node a = MakeTarget(1, "LargeBlockConveyor");
        Node b = MakeTarget(2, "LargeBlockConveyor");
        Node outside = MakeTarget(3, "LargeBlockConveyor");
        graph.AddNode(a);
        graph.AddNode(b);
        graph.AddNode(outside);

        a.Position = new Vector2(0.10f, 0.20f);
        b.Position = new Vector2(0.40f, 0.35f);
        outside.Position = new Vector2(0.80f, 0.10f);

        var members = new List<long> { 1, 2 };
        var actions = new List<GroupMemberActions> { EnabledMember("MyConveyor", false, "OnOff") };
        Node group = graph.SyncGroup("Belt", members, actions);
        graph.LayoutPorts();
        PortGeometry.LayoutGroupFrames(graph);

        Vector2 frameBefore, sizeBefore;
        Check(PortGeometry.TryBodyRect(group, out frameBefore, out sizeBefore), "the group has a frame before the drag");

        // The relative layout of the members, recorded so the drag can be checked against it.
        Vector2 relativeBefore = b.Position - a.Position;

        Vector2 delta = new Vector2(0.07f, -0.03f);
        Check(graph.OffsetGroupMembers(group, delta) == 2,
              "dragging a group moves every drawn member");

        CheckClose(a.Position.X, 0.10f + delta.X, "the first member moves by the drag offset in X");
        CheckClose(a.Position.Y, 0.20f + delta.Y, "the first member moves by the drag offset in Y");
        CheckClose(b.Position.X, 0.40f + delta.X, "the second member moves by the same offset in X");
        CheckClose(b.Position.Y, 0.35f + delta.Y, "the second member moves by the same offset in Y");

        Vector2 relativeAfter = b.Position - a.Position;
        CheckClose(relativeAfter.X, relativeBefore.X, "the members keep their relative X");
        CheckClose(relativeAfter.Y, relativeBefore.Y, "the members keep their relative Y");

        CheckClose(outside.Position.X, 0.80f, "a card outside the group does not move in X");
        CheckClose(outside.Position.Y, 0.10f, "a card outside the group does not move in Y");

        // The frame is derived, so it has to follow the cards — and only translate, not resize:
        // a translation cannot change the cluster's extent.
        PortGeometry.LayoutGroupFrames(graph);
        Vector2 frameAfter, sizeAfter;
        Check(PortGeometry.TryBodyRect(group, out frameAfter, out sizeAfter), "the group still has a frame");
        CheckClose(frameAfter.X, frameBefore.X + delta.X, "the frame follows the group horizontally");
        CheckClose(frameAfter.Y, frameBefore.Y + delta.Y, "the frame follows the group vertically");
        CheckClose(sizeAfter.X, sizeBefore.X, "a translated group keeps its width");
        CheckClose(sizeAfter.Y, sizeBefore.Y, "a translated group keeps its height");

        // A member that is not drawn has no card, so it is not in Members and must not move:
        // the canvas moves what the player can see, and a hidden block must stay where its own
        // stored position says.
        var partialMembers = new List<long> { 1 };
        Node partial = graph.SyncGroup("Belt", partialMembers, actions);
        Vector2 beforeHidden = b.Position;
        Check(graph.OffsetGroupMembers(partial, new Vector2(0.05f, 0.05f)) == 1,
              "a group with one drawn member moves one card");
        CheckClose(b.Position.X, beforeHidden.X, "a member that is not drawn does not move in X");
        CheckClose(b.Position.Y, beforeHidden.Y, "a member that is not drawn does not move in Y");

        // Degenerate inputs: no offset, and a node that is not a group. Both are no-ops rather
        // than errors, because the caller is a pointer handler.
        Check(graph.OffsetGroupMembers(partial, Vector2.Zero) == 0, "a zero offset moves nothing");
        Check(graph.OffsetGroupMembers(a, new Vector2(0.1f, 0.1f)) == 0, "a card is not a group");

        // The title strip is the drag handle, and only it: the frame's interior stays canvas so
        // the cluster can still be panned across.
        PortGeometry.LayoutGroupFrames(graph);
        Vector2 titleTopLeft, titleSize;
        Check(PortGeometry.TryGroupTitleRect(partial, out titleTopLeft, out titleSize),
              "the group has a title strip to grab");
        Check(PortGeometry.HitGroupTitle(partial, titleTopLeft + new Vector2(titleSize.X * 0.5f, titleSize.Y * 0.5f)),
              "the title strip is a drag handle");
        Check(!PortGeometry.HitGroupTitle(partial, titleTopLeft + new Vector2(titleSize.X * 0.5f, titleSize.Y + 0.05f)),
              "the strip below the title is not a drag handle");
        Check(!PortGeometry.HitGroupTitle(a, a.Position + new Vector2(0.01f, 0.01f)),
              "a card has no title strip");
    }

    /// <summary>
    /// The watch button of an event controller and the watch ports under its card (ADR-0017,
    /// ADR-0020).
    ///
    /// What matters and is invisible in a compile: the watch button is a button of the card's row
    /// like any other — same size, same vertical centre, right-hand side — and it exists on a
    /// controller and nowhere else; the watch bars stack under the input stack with the dot on
    /// the right and the "x" on the left.
    /// </summary>
    private static void WatchAnchorAndPorts()
    {
        var graph = new GridGraph(21);

        // A controller: a yes/no toolbar like the game builds for it, and the flag that makes it
        // a controller (read from the game's interface in the real build).
        var controller = new Node(1, "EventControllerLarge", "controller", Vector3I.Zero,
                                  SensorToolbar(), true);
        BlockPortCatalog.FillEvents(controller);
        controller.Position = new Vector2(0.30f, 0.20f);

        Node watched = MakeTarget(2, "LargeBlockSmallGenerator", "OnOff");
        watched.Position = new Vector2(0.70f, 0.20f);

        graph.AddNode(controller);
        graph.AddNode(watched);

        const float aspect = 1f;

        // The card is one line taller, because the mode description needs a line.
        float cardHeight = PortGeometry.CardHeight(controller);
        Check(cardHeight > VgsConstants.NodeHeight, "a controller's card carries the extra line");
        CheckClose(cardHeight, VgsConstants.NodeHeight + VgsConstants.NodeStatusLineHeight,
                   "the extra line is exactly NodeStatusLineHeight tall");

        Vector2 anchorTopLeft, anchorSize;
        Check(PortGeometry.TryCardButtonRect(controller, CardButtonKind.Watch, aspect,
                                             out anchorTopLeft, out anchorSize),
              "a controller has a watch button");

        // The watch button is one of the card's buttons, so it is the standard size — not a
        // square grown with the taller card, which is what it used to be.
        CheckClose(anchorSize.Y, VgsConstants.CardButtonDiameter,
                   "the watch button is the standard card button diameter");
        CheckClose(anchorSize.X, anchorSize.Y, "the watch button is round on screen at aspect 1");

        // It sits inside the card, on its right-hand side, vertically centred.
        Check(anchorTopLeft.Y >= controller.Position.Y - 1e-6f,
              "the watch button does not leave the card at the top");
        Check(anchorTopLeft.Y + anchorSize.Y <= controller.Position.Y + cardHeight + 1e-6f,
              "the watch button does not leave the card at the bottom");

        float above = anchorTopLeft.Y - controller.Position.Y;
        float below = controller.Position.Y + cardHeight - (anchorTopLeft.Y + anchorSize.Y);
        CheckClose(above, below, "the watch button is vertically centred on the card");

        // It is on the right: the row is right-anchored, so the watch button — the second of a
        // four-button row — still sits well inside the card, past the start of the name. The
        // old "#" was pinned to the left border instead, which is the placement this row ends.
        Check(anchorTopLeft.X > controller.Position.X + VgsConstants.NodeWidth * 0.3f,
              "the watch button sits inside the card's right-hand row, not on the left border");

        // The centre of the button is the point a watch line starts from while the user picks a
        // block, so it must be the centre of the button, not its corner.
        Vector2 anchorCentre;
        Check(PortGeometry.TryWatchButton(controller, aspect, out anchorCentre),
              "the watch button has a centre");
        CheckClose(anchorCentre.X, anchorTopLeft.X + anchorSize.X * 0.5f,
                   "the centre is the button's centre in X");
        CheckClose(anchorCentre.Y, anchorTopLeft.Y + anchorSize.Y * 0.5f,
                   "the centre is the button's centre in Y");

        CardButtonKind hitKind;
        Check(PortGeometry.HitCardButton(controller, anchorCentre, aspect, out hitKind)
              && hitKind == CardButtonKind.Watch,
              "clicking the watch button hits it");
        Check(!PortGeometry.HitCardButton(controller, anchorCentre + new Vector2(0.2f, 0f), aspect,
                                          out hitKind),
              "clicking far to the right of the watch button misses the row");

        // The button is round on screen at any window shape: its X extent is the Y extent times
        // the aspect, so the two are equal once the aspect is taken out.
        float[] aspects = { 0.5625f, 0.75f, 1f, 1.6f };
        for (int i = 0; i < aspects.Length; i++)
        {
            float a = aspects[i];
            Vector2 t, s;
            Check(PortGeometry.TryCardButtonRect(controller, CardButtonKind.Watch, a, out t, out s),
                  "watch button rectangle at aspect " + a);
            CheckClose(s.X / a, s.Y, "the watch button is round on screen at aspect " + a);
        }

        // A block that is not a controller has no watch button, even with a yes/no toolbar.
        Check(!PortGeometry.HasCardButton(watched, CardButtonKind.Watch),
              "a block that is not a controller has no watch button");
        Check(!PortGeometry.TryWatchButton(watched, aspect, out anchorCentre),
              "there is no watch button to start a watch line from");

        // It does carry the jump button, though — every block does (ADR-0021) — so the only dead
        // part of its card is the body, where the name is.
        Check(PortGeometry.HasCardButton(watched, CardButtonKind.Jump),
              "a plain block still has the jump button");
        Vector2 watchedJump;
        Check(PortGeometry.TryCardButton(watched, CardButtonKind.Jump, aspect, out watchedJump),
              "the plain block's jump button has a centre");
        Check(PortGeometry.HitCardButton(watched, watchedJump, aspect, out hitKind)
              && hitKind == CardButtonKind.Jump,
              "the plain block's jump button is clickable");
        Check(!PortGeometry.HitCardButton(watched, watched.Position, aspect, out hitKind),
              "the body of a card is not a button");

        // ...and its card keeps the plain height: only a controller grows by the extra line.
        CheckClose(PortGeometry.CardHeight(watched), VgsConstants.NodeHeight,
                   "a plain block's card does not grow");

        // ---- the watch list and its bars ----

        Check(graph.TryAddWatch(controller, 2, watched.DisplayName), "a block can be watched");
        Check(graph.HasWatch(controller, 2), "the controller now watches that block");
        Check(!graph.TryAddWatch(controller, 2, watched.DisplayName), "the same block cannot be watched twice");
        Check(!graph.TryAddWatch(watched, 2, "self"), "a block that is not a controller cannot watch anything");
        Check(!graph.TryAddWatch(controller, 0, "zero"), "entity id zero is not a block");

        // The slot is assigned by the layout pass, from the list, with no edge involved.
        graph.LayoutPorts();
        WatchTarget target = controller.Controller.FindWatch(2);
        Check(target != null, "the watch target is in the list");
        Check(target.Slot == 0, "the first watch target takes the first watch slot");
        Check(PortGeometry.DrawnWatchCount(controller) == 1, "one watch bar is drawn");

        Vector2 barTopLeft, barSize;
        Check(PortGeometry.TryWatchBarRect(controller, target, out barTopLeft, out barSize),
              "the watch bar has a rectangle");
        CheckClose(barTopLeft.X, controller.Position.X, "the watch bar starts at the card's left edge");
        CheckClose(barSize.X, VgsConstants.NodeWidth, "the watch bar is as wide as the card");

        // Under the card, and below any stack that is already there: the card and its own
        // actions stay the first thing under it.
        float minimumTop = controller.Position.Y + cardHeight
                           + PortGeometry.OutputStackHeight(controller)
                           + PortGeometry.InputStackHeight(controller);
        Check(barTopLeft.Y >= minimumTop - 1e-6f, "the watch bar sits under the card and its stacks");

        // The dot is on the right (the line leaves for the watched block), the "x" on the left.
        Vector2 dot, remove;
        Check(PortGeometry.TryWatchDot(controller, target, aspect, out dot), "the watch bar has a dot");
        Check(PortGeometry.ShouldDrawWatchLine(controller, target),
              "a watch of another block draws its dashed line");
        Check(PortGeometry.TryWatchRemoveButton(controller, target, aspect, out remove),
              "the watch bar has a remove button");
        Check(dot.X > remove.X, "the dot is to the right of the remove button");
        CheckClose(dot.Y, barTopLeft.Y + barSize.Y * 0.5f, "the dot is centred on the bar");
        CheckClose(remove.Y, dot.Y, "the remove button is centred on the bar");

        float inset = ControlOffset(aspect);
        CheckClose(dot.X, barTopLeft.X + barSize.X - inset, "the dot is inset from the bar's right edge");
        CheckClose(remove.X, barTopLeft.X + inset, "the remove button is inset from the bar's left edge");

        Check(dot.X + ControlHalfWidth(aspect) <= barTopLeft.X + barSize.X + 1e-6f,
              "the dot stays inside its bar");
        Check(remove.X - ControlHalfWidth(aspect) >= barTopLeft.X - 1e-6f,
              "the remove button stays inside its bar");

        // The label is the watched block's name, between the two controls.
        Vector2 labelTopLeft, labelSize;
        Check(PortGeometry.TryWatchLabelRect(controller, target, aspect, out labelTopLeft, out labelSize),
              "the watch bar has a label rectangle");
        Check(labelTopLeft.X > remove.X, "the label starts right of the remove button");
        Check(labelTopLeft.X + labelSize.X < dot.X, "the label ends left of the dot");

        // Hit testing: the bar belongs to the node, and the "x" reports its own target.
        Check(PortGeometry.HitNode(controller, barTopLeft + new Vector2(barSize.X * 0.5f, barSize.Y * 0.5f)),
              "the watch bar belongs to the controller for hit-testing");
        Check(ReferenceEquals(PortGeometry.HitWatchRemoveButton(controller, remove, aspect), target),
              "the watch remove button reports its own target");
        Check(PortGeometry.HitWatchRemoveButton(controller, dot, aspect) == null,
              "the watch dot is not the remove button");

        // Total height covers the watch stack as well.
        float total = PortGeometry.TotalHeight(controller);
        Check(total >= cardHeight + barSize.Y - 1e-6f, "the node's height includes the watch stack");

        // A second watched block stacks below the first, contiguously.
        Node second = MakeTarget(3, "LargeBlockSmallGenerator", "OnOff");
        second.Position = new Vector2(0.70f, 0.40f);
        graph.AddNode(second);
        Check(graph.TryAddWatch(controller, 3, second.DisplayName), "a second block can be watched");
        graph.LayoutPorts();
        Check(controller.Controller.FindWatch(3).Slot == 1, "the second watch target takes the second slot");

        Vector2 secondTopLeft, secondSize;
        Check(PortGeometry.TryWatchBarRect(controller, controller.Controller.FindWatch(3), out secondTopLeft, out secondSize),
              "the second watch bar has a rectangle");
        CheckClose(secondTopLeft.Y, barTopLeft.Y + barSize.Y, "the watch bars stack flush");

        // Removing a block closes the gap: slots are reassigned from the list, not kept.
        Check(graph.RemoveWatch(controller, 2), "a watched block can be removed");
        Check(!graph.HasWatch(controller, 2), "the controller no longer watches it");
        Check(!graph.RemoveWatch(controller, 2), "removing a block that is not watched does nothing");
        graph.LayoutPorts();
        Check(controller.Controller.FindWatch(3).Slot == 0, "the remaining watch target moves up to the first slot");
        Check(PortGeometry.DrawnWatchCount(controller) == 1, "one watch bar is still drawn");

        // Removing the last one clears the slot too: no bar survives an empty list.
        Check(graph.RemoveWatch(controller, 3), "the last watched block can be removed");
        graph.LayoutPorts();
        Check(PortGeometry.DrawnWatchCount(controller) == 0, "an empty watch list draws no bars");
        CheckClose(PortGeometry.WatchStackHeight(controller), 0f, "an empty watch list has no stack");

        // A controller may watch itself (ADR-0036): the engine's ValidateBlock only asks for the
        // same construct, and the event component subscribes it like any other block. The bar is
        // the whole picture — the draw pass omits the dashed line, which would run from the bar
        // into the controller's own card.
        Check(graph.TryAddWatch(controller, controller.EntityId, controller.DisplayName),
              "a controller may watch itself");
        Check(graph.HasWatch(controller, controller.EntityId), "the controller now watches itself");
        Check(!graph.TryAddWatch(controller, controller.EntityId, "again"),
              "a self watch is not added twice");
        graph.LayoutPorts();
        WatchTarget self = controller.Controller.FindWatch(controller.EntityId);
        Check(self != null, "the self watch is in the list");
        Check(self.Slot == 0, "the self watch takes the first slot of an empty stack");
        Check(PortGeometry.DrawnWatchCount(controller) == 1, "the self watch draws a bar");
        Check(self.DisplayName == controller.DisplayName,
              "the self watch's bar carries the controller's own name");
        Check(!PortGeometry.ShouldDrawWatchLine(controller, self),
              "a self watch draws no dashed line — the bar is the picture");
        Check(!PortGeometry.ShouldDrawWatchLine(controller, null),
              "a missing watch target has no line");
        Vector2 selfBarTopLeft, selfBarSize;
        Check(PortGeometry.TryWatchBarRect(controller, self, out selfBarTopLeft, out selfBarSize),
              "the self watch has a bar rectangle");
        Check(graph.RemoveWatch(controller, controller.EntityId), "the self watch can be removed");
        graph.LayoutPorts();
        Check(PortGeometry.DrawnWatchCount(controller) == 0, "removing the self watch clears its bar");

        // The stack is capped, and the cap is enforced on attach rather than on draw: a bar with
        // no drawing slot would have no "x", and the player could never detach that block.
        var full = new Node(90, "EventControllerLarge", "full", Vector3I.Zero, SensorToolbar(), true);
        full.Position = new Vector2(0.1f, 0.8f);
        graph.AddNode(full);
        for (int i = 0; i < GridGraph.MaxPortsPerSide; i++)
        {
            Check(graph.TryAddWatch(full, 100 + i, "b" + i), "watch " + i + " fits");
        }
        Check(!graph.TryAddWatch(full, 200, "overflow"),
              "the watch stack refuses more than MaxPortsPerSide blocks");
        graph.LayoutPorts();
        Check(PortGeometry.DrawnWatchCount(full) == GridGraph.MaxPortsPerSide,
              "every accepted watch gets a bar — none is silently invisible");

        // Collecting the ids is what the storage writer consumes.
        var ids = new List<long>();
        GridGraph.CollectWatchedIds(full, ids);
        Check(ids.Count == GridGraph.MaxPortsPerSide, "the collected list holds every accepted id");

        // A watched block's card centre is where the watch line ends.
        Vector2 centre;
        Check(PortGeometry.TryCardCentre(watched, out centre), "the watched card has a centre");
        CheckClose(centre.X, watched.Position.X + VgsConstants.NodeWidth * 0.5f, "the card centre in X");
        CheckClose(centre.Y, watched.Position.Y + PortGeometry.CardHeight(watched) * 0.5f,
                   "the card centre in Y");
    }

    /// <summary>
    /// The dash pattern of a watch line (ADR-0017). It is pure geometry, so it is checkable
    /// without the game — and it has to be, because a dash pattern that overruns its segment
    /// draws a line past the card it is heading for.
    /// </summary>
    private static void WatchDashes()
    {
        var dashes = new List<Vector4>();

        // A horizontal segment of known length with a pattern that divides it evenly.
        const float dash = 0.01f;
        const float gap = 0.01f;
        var segment = new Vector4(0.10f, 0.50f, 0.30f, 0.50f);   // 0.20 long
        EdgeRouting.BuildDashes(segment, dash, gap, dashes);

        Check(dashes.Count == 10, "a 0.20 segment with a 0.02 period yields ten dashes");
        CheckClose(dashes[0].X, 0.10f, "the first dash starts at the segment's start");
        CheckClose(dashes[0].Z, 0.10f + dash, "the first dash is one dash long");
        CheckClose(dashes[0].Y, 0.50f, "a horizontal dash keeps the segment's Y");

        // The pattern may end on a gap — that is what a dash pattern does — but it must not end
        // earlier than one period before the end, or the line would look truncated.
        Check(dashes[dashes.Count - 1].Z <= segment.Z + 1e-6f,
              "the last dash does not pass the segment's end");
        Check(dashes[dashes.Count - 1].Z > segment.Z - dash - gap,
              "the last dash reaches within one period of the segment's end");

        // No dash overruns its neighbour, and none leaves the segment.
        for (int i = 0; i < dashes.Count; i++)
        {
            Check(dashes[i].Z >= dashes[i].X, "a dash is not negative");
            Check(dashes[i].Z <= segment.Z + 1e-6f, "a dash does not leave the segment");
            if (i > 0) Check(dashes[i].X >= dashes[i - 1].Z, "dashes do not overlap");
        }

        // A segment shorter than one dash is drawn as one dash, not as nothing.
        dashes.Clear();
        EdgeRouting.BuildDashes(new Vector4(0f, 0f, 0.004f, 0f), dash, gap, dashes);
        Check(dashes.Count == 1, "a segment shorter than a dash is one dash");
        CheckClose(dashes[0].Z, 0.004f, "the shortened dash ends at the segment's end");

        // Vertical segments go through the same arithmetic, along Y.
        dashes.Clear();
        EdgeRouting.BuildDashes(new Vector4(0.5f, 0.1f, 0.5f, 0.3f), dash, gap, dashes);
        Check(dashes.Count == 10, "a vertical segment is dashed like a horizontal one");
        CheckClose(dashes[0].X, 0.5f, "a vertical dash keeps the segment's X");
        CheckClose(dashes[0].Y, 0.1f, "a vertical dash starts at the segment's start");

        // Degenerate inputs: no length, and a zero dash length (draw it solid rather than drop).
        dashes.Clear();
        EdgeRouting.BuildDashes(new Vector4(0.2f, 0.2f, 0.2f, 0.2f), dash, gap, dashes);
        Check(dashes.Count == 0, "a zero-length segment has no dashes");

        // A zero dash length draws the segment solid.
        dashes.Clear();
        EdgeRouting.BuildDashes(segment, 0f, gap, dashes);
        Check(dashes.Count == 1, "a zero dash length draws the segment solid");

        // Reusing one buffer must not accumulate: the draw path reuses a single static list for
        // every watch line of every frame, and appending would redraw every dash ever produced.
        dashes.Clear();
        EdgeRouting.BuildDashes(segment, dash, gap, dashes);
        int first = dashes.Count;
        EdgeRouting.BuildDashes(segment, dash, gap, dashes);
        Check(dashes.Count == first, "reusing the dash buffer replaces its contents");
    }

    /// <summary>
    /// The wire format of an event controller's watch list (ADR-0017): ids separated by spaces,
    /// tolerant on read, invariant on write.
    /// </summary>
    private static void WatchListFormat()
    {
        var ids = new List<long> { 1234567890123L, 42L, 7L };
        string text = NodeStateFormat.FormatIds(ids);

        Check(text == "1234567890123 42 7", "the list is written space separated");

        var back = new List<long>();
        NodeStateFormat.ParseIds(text, back);
        Check(back.Count == 3, "all three ids come back");
        Check(back[0] == 1234567890123L && back[1] == 42L && back[2] == 7L,
              "the ids come back in order");

        // An empty list is an empty string, and an empty string is an empty list.
        Check(NodeStateFormat.FormatIds(new List<long>()) == string.Empty,
              "an empty list is an empty string");
        Check(NodeStateFormat.FormatIds(null) == string.Empty, "a null list is an empty string");

        back.Add(999);
        NodeStateFormat.ParseIds(string.Empty, back);
        Check(back.Count == 0, "an empty string clears the list");

        NodeStateFormat.ParseIds(null, back);
        Check(back.Count == 0, "a null string clears the list");

        // Tolerant on read: garbage costs its own token, not the whole list.
        back.Clear();
        NodeStateFormat.ParseIds("12  garbage  34", back);
        Check(back.Count == 2 && back[0] == 12 && back[1] == 34,
              "an unreadable token is skipped, the rest survives");

        // A set has no duplicates, and no entity has id zero or a negative id.
        back.Clear();
        NodeStateFormat.ParseIds("5 5 0 -3 6", back);
        Check(back.Count == 2 && back[0] == 5 && back[1] == 6,
              "duplicates, zero and negative ids are dropped");

        // Leading and trailing separators are not a format change: they produce no token.
        back.Clear();
        NodeStateFormat.ParseIds(" 8  9 ", back);
        Check(back.Count == 2 && back[0] == 8 && back[1] == 9,
              "extra separators are harmless");

        // Round trip through the storage format is stable, which is the property a save/load
        // cycle depends on.
        back.Clear();
        NodeStateFormat.ParseIds(NodeStateFormat.FormatIds(ids), back);
        Check(NodeStateFormat.FormatIds(back) == text, "the round trip is stable");
    }

    /// <summary>
    /// The rule that makes the schema follow the game (ADR-0018): the displayed list is the union
    /// of what the controller reports and what the editor added, and an entry the game has adopted
    /// stops being an editor addition.
    ///
    /// This is the part of the feature a player notices first — change the selection in the
    /// terminal, open the editor, and the ports must match — and it is pure data, so it is
    /// checked here rather than in the game.
    /// </summary>
    private static void WatchReconciliation()
    {
        var game = new List<long>();
        var overlay = new List<long>();
        var merged = new List<long>();

        // Terminal-only: the game's order is kept.
        game.AddRange(new long[] { 30, 10, 20 });
        GridGraph.MergeWatchIds(game, overlay, merged);
        Check(merged.Count == 3, "a terminal-only list is shown as it is");
        Check(merged[0] == 30 && merged[1] == 10 && merged[2] == 20,
              "the game's order is preserved, not sorted");

        // Editor-only: what the game does not store is still shown.
        game.Clear();
        overlay.Clear();
        overlay.AddRange(new long[] { 5, 6 });
        GridGraph.MergeWatchIds(game, overlay, merged);
        Check(merged.Count == 2 && merged[0] == 5 && merged[1] == 6,
              "editor-added entries are shown after the game's");

        // Both: the game's entries come first, and nothing is duplicated.
        game.Clear();
        overlay.Clear();
        game.AddRange(new long[] { 10, 20 });
        overlay.AddRange(new long[] { 5, 20, 7 });
        GridGraph.MergeWatchIds(game, overlay, merged);
        Check(merged.Count == 4, "the union has no duplicates");
        Check(merged[0] == 10 && merged[1] == 20, "game entries keep the first slots");
        Check(merged[2] == 5 && merged[3] == 7, "editor entries follow, in their own order");

        // Garbage is not a block: zero, negatives and repeats never reach the list.
        game.Clear();
        overlay.Clear();
        game.AddRange(new long[] { 0, -3, 10, 10 });
        overlay.AddRange(new long[] { 0, 10 });
        GridGraph.MergeWatchIds(game, overlay, merged);
        Check(merged.Count == 1 && merged[0] == 10, "zero, negative and duplicate ids are dropped");

        // Null sources are empty, not an exception: the caller reads them from the world.
        merged.Add(999);
        GridGraph.MergeWatchIds(null, null, merged);
        Check(merged.Count == 0, "missing sources clear the list instead of throwing");

        // The game adopts an id the editor added (the player also selected it in the terminal):
        // the editor must forget it, or it would be counted twice and replayed after a reload.
        game.Clear();
        overlay.Clear();
        game.AddRange(new long[] { 10, 20 });
        overlay.AddRange(new long[] { 20, 30 });
        Check(GridGraph.PruneAdoptedWatchIds(game, overlay), "an adopted id is pruned from the overlay");
        Check(overlay.Count == 1 && overlay[0] == 30, "only the editor-owned id survives");
        Check(!GridGraph.PruneAdoptedWatchIds(game, overlay), "pruning again changes nothing");

        // Nothing adopted: the overlay is untouched.
        game.Clear();
        overlay.Clear();
        game.Add(10);
        overlay.Add(30);
        Check(!GridGraph.PruneAdoptedWatchIds(game, overlay), "an unrelated id is not pruned");
        Check(overlay.Count == 1, "the overlay keeps its entry");

        // An empty overlay and an empty game list are both fine.
        game.Clear();
        overlay.Clear();
        Check(!GridGraph.PruneAdoptedWatchIds(game, overlay), "pruning two empty lists does nothing");

        // ---- SyncWatched: the ids are the truth, the list is the view ----

        var controller = new Node(1, "EventControllerLarge", "controller", Vector3I.Zero,
                                  SensorToolbar(), true);
        controller.Position = new Vector2(0.2f, 0.2f);

        var ids = new List<long> { 10, 20 };
        GridGraph.SyncWatched(controller, ids, id => "block" + id);
        Check(controller.Controller.Watched.Count == 2, "two ids make two watch targets");
        Check(controller.Controller.Watched[0].EntityId == 10 && controller.Controller.Watched[1].EntityId == 20,
              "the targets follow the ids' order");
        Check(controller.Controller.Watched[0].DisplayName == "block10", "the name comes from the resolver");

        // The surviving target object is reused, so a hover held by reference stays valid.
        WatchTarget kept = controller.Controller.Watched[0];
        var ids2 = new List<long> { 10, 30 };
        GridGraph.SyncWatched(controller, ids2, id => "renamed" + id);
        Check(controller.Controller.Watched.Count == 2, "a changed list is rebuilt to the new ids");
        Check(ReferenceEquals(controller.Controller.Watched[0], kept),
              "a surviving watch target keeps its object");
        Check(controller.Controller.Watched[0].DisplayName == "renamed10", "a surviving target gets its new name");
        Check(controller.Controller.Watched[1].EntityId == 30, "the new id appears");

        // An id that disappears loses its target — that is how a removal in the terminal reaches
        // the schema.
        var ids3 = new List<long> { 30 };
        GridGraph.SyncWatched(controller, ids3, id => "block" + id);
        Check(controller.Controller.Watched.Count == 1 && controller.Controller.Watched[0].EntityId == 30,
              "an id the game no longer reports loses its port");

        // An empty list empties the stack, and a null resolver does not throw.
        GridGraph.SyncWatched(controller, new List<long>(), id => "x");
        Check(controller.Controller.Watched.Count == 0, "an empty id list draws no ports");

        GridGraph.SyncWatched(controller, new List<long> { 40 }, null);
        Check(controller.Controller.Watched.Count == 1, "a missing resolver still adds the target");
        Check(controller.Controller.Watched[0].DisplayName == "40",
              "without a name the bar falls back to the id, not to an empty label");
    }

    // ------------------------------------------------------------------ icon atlas (ADR-0019)
    //
    // The atlas is built by tools/build-icon-atlas.ps1 and its table is generated, so what is
    // asserted here is the contract between that table and the drawing code: every enum member
    // resolves, every cell lies inside the texture, no two cells overlap, and the numbers the
    // drawing code multiplies are not degenerate. A wrong cell shows the neighbouring icon; a
    // cell outside the texture draws nothing at all; a zero aspect collapses the destination.
    private static void IconAtlasTable()
    {
        Check(VgsIconAtlas.Count > 0, "the icon build produced at least one icon");

        var members = (VgsIcon[])Enum.GetValues(typeof(VgsIcon));

        // A drift detector, not a behaviour check: both numbers come from the same generator, so
        // this can only fail after a hand edit — which is exactly what it is for.
        Check(members.Length == VgsIconAtlas.Count,
              "the VgsIcon enum and the generated table agree on the icon count");

        Check(VgsIconAtlas.TextureSize > 0, "the icon textures have a size");

        // The texture is square and is drawn square, so its side must be a multiple of 4: DXT5
        // compresses 4x4 blocks, and a size that is not a multiple of 4 pads every block.
        Check(VgsIconAtlas.TextureSize % 4 == 0,
              "the texture side is a multiple of 4, so the DXT5 blocks are not padded");

        var names = new List<string>();

        for (int i = 0; i < members.Length; i++)
        {
            VgsIcon icon = members[i];

            Rectangle source;
            Check(VgsIconAtlas.TryGetSource(icon, out source),
                  "every VgsIcon member has a source rectangle (" + icon + ")");

            // One texture per icon: the whole of it is the source, at its origin.
            Check(source.X == 0 && source.Y == 0
                  && source.Width == VgsIconAtlas.TextureSize
                  && source.Height == VgsIconAtlas.TextureSize,
                  "every icon draws from a whole texture, not from a cell of one (" + icon + ")");

            string name = VgsIconAtlas.MaterialName(icon);
            Check(!string.IsNullOrEmpty(name), "every icon has a material name (" + icon + ")");
            Check(name.StartsWith(VgsIconAtlas.MaterialPrefix),
                  "every material name carries the mod prefix, so it cannot collide with a "
                  + "vanilla SubtypeName (" + icon + ")");

            // Two icons sharing a SubtypeName would resolve to one texture and one of them would
            // silently draw the other's picture.
            Check(!names.Contains(name),
                  "no two icons share a material name (" + icon + ")");
            names.Add(name);

            float textureScale = VgsIconAtlas.TextureScale(icon);
            Check(textureScale >= 1f && textureScale < 2f,
                  "the texture is at least as large as the ink it contains, and not absurdly "
                  + "larger (" + icon + ")");

            // The ink must fill the texture in its *longest* dimension; the padding in the other
            // one is unavoidable and is what keeps the icon's proportions. So the margin is
            // measured against the ink's own aspect rather than against a constant: a square icon
            // fills the texture both ways, and a wide one — the card's toggle is 1.5 wide for every
            // unit of height — fills it across and is padded above and below by construction.
            //
            // The bound is a drift detector, not a behaviour: both numbers come from the same
            // generator, so it can only fail after a hand edit or a redrawn icon.
            float fill = textureScale / Math.Max(1f, VgsIconAtlas.InkAspect(icon));
            Check(fill >= 1.02f && fill <= 1.10f,
                  "the ink fills the texture in its longest dimension, so the margin is the inset "
                  + "and not extra padding (" + icon + ")");

            Check(VgsIconAtlas.InkAspect(icon) > 0f,
                  "every icon's ink has a positive aspect (" + icon + ")");
        }

        // The lookup has to refuse a value that is not a member, or an icon added to the enum but
        // not to the build would silently draw the first texture.
        Rectangle unknown;
        Check(!VgsIconAtlas.TryGetSource((VgsIcon)999, out unknown),
              "an unknown icon has no source, rather than the first one");
        Check(!VgsIconAtlas.TryGetSource((VgsIcon)(-1), out unknown),
              "a negative icon value is refused, not treated as an index");
    }

    // ------------------------------------------------------------------ icon placement
    //
    // The three ways an icon ends up in the wrong place, each asserted separately: wrong pixel
    // space, fractional position, and clipping that moves the icon instead of cutting it.
    private static void IconPlacementChecks()
    {
        const float width = 1920f;
        const float height = 1080f;

        var canvasTopLeft = new Vector2(0.1f, 0.12f);
        var canvasSize = new Vector2(0.8f, 0.7f);

        // ---- a whole icon, inside the canvas ----

        Rectangle destination, clip;
        bool placed = IconPlacement.TryPlace(new Vector2(0.5f, 0.5f), 0.0135f, 1f, width, height,
                                             canvasTopLeft, canvasSize, out destination, out clip);
        Check(placed, "an icon inside the canvas is placed");

        // 0.0135 of 1080 is 14.58 px, which rounds to 15.
        Check(destination.Width == 15 && destination.Height == 15,
              "the icon is drawn at its ink height in screen pixels");
        Check(destination.X + destination.Width / 2 == (int)Math.Round(0.5f * width),
              "the icon is centred horizontally on the requested point, in window pixels");
        Check(destination.Y + destination.Height / 2 == (int)Math.Round(0.5f * height),
              "the icon is centred vertically on the requested point, in window pixels");

        // Stated as intent, not as the formula above: whatever the rounding, the drawn centre must
        // be within half a pixel of the point the caller asked for. An odd side puts the exact
        // centre on a half pixel, so zero would be asserting something impossible.
        Check(Math.Abs(destination.X + destination.Width / 2f - 0.5f * width) <= 0.5f
              && Math.Abs(destination.Y + destination.Height / 2f - 0.5f * height) <= 0.5f,
              "the drawn centre is within half a pixel of the requested centre");

        // ---- the pixel space is the window, not the 4:3 safe area ----
        //
        // On 16:9 the safe-GUI box is 1440 wide inside a 1920 window, so a normalised 0.5 would
        // land at 720 instead of 960 if the conversion were done in the wrong space. That is the
        // defect this check exists for.
        Check(destination.X + destination.Width / 2 > 900,
              "the centre is computed in window pixels, not in the 4:3 safe area");

        // A source rectangle whose origin is NOT square, so a bug that reads source.X where it
        // means source.Y (or the reverse) cannot hide behind two equal numbers.
        var offsetSource = new Rectangle(40, 12, 200, 160);

        Rectangle source = new Rectangle(8, 8, 256, 256);
        Rectangle visibleDestination, visibleSource;
        Check(IconPlacement.TryClip(destination, source, clip, out visibleDestination, out visibleSource),
              "an icon fully inside the canvas is not clipped");
        Check(visibleDestination == destination, "an unclipped destination is unchanged");
        Check(visibleSource == source, "an unclipped source is unchanged");

        // ---- a fractional position is snapped ----
        //
        // The same height at a zoom that produces a fractional pixel size: the destination must
        // still be whole pixels, or the filter blurs the icon.
        Rectangle fractional, fractionalClip;
        Check(IconPlacement.TryPlace(new Vector2(0.31337f, 0.27111f), 0.0131f, 1f, width, height,
                                     canvasTopLeft, canvasSize, out fractional, out fractionalClip),
              "a fractional centre still places the icon");
        Check(fractional.Width == fractional.Height,
              "the quad is drawn square, because the texture is square");
        Check(fractional.X == (int)fractional.X && fractional.Y == (int)fractional.Y,
              "the destination starts on a whole pixel");

        // ---- clipping cuts both rectangles by the same fraction ----
        //
        // The icon is placed so that its left half falls outside the canvas.
        var farLeft = new Vector2(canvasTopLeft.X - 0.002f, 0.5f);
        Rectangle cutDestination, cutClip;
        Check(IconPlacement.TryPlace(farLeft, 0.0135f, 1f, width, height,
                                     canvasTopLeft, canvasSize, out cutDestination, out cutClip),
              "an icon straddling the canvas edge is still placed");

        Check(IconPlacement.TryClip(cutDestination, source, cutClip,
                                    out visibleDestination, out visibleSource),
              "a straddling icon is clipped, not dropped");

        Check(visibleDestination.Width < cutDestination.Width,
              "the visible part is narrower than the whole icon");
        Check(visibleSource.Width < source.Width,
              "the source is cut by the same amount, so the icon does not slide");

        // The two fractions must agree to within the rounding of one source pixel: the source is
        // 256 texels wide and the destination a handful of pixels, so exact equality is not
        // reachable and insisting on it would be asserting the wrong thing.
        float destinationFraction = (float)visibleDestination.Width / cutDestination.Width;
        float sourceFraction = (float)visibleSource.Width / source.Width;
        Check(Math.Abs(destinationFraction - sourceFraction) <= 0.5f / source.Width + 1e-5f,
              "the visible fraction of the source equals the visible fraction of the destination "
              + "(within one texel)");

        // ---- an off-centre source keeps its origin ----
        //
        // The cut is applied to the source's own origin, not to zero: a cell that does not start
        // at (0,0) must still yield a source rectangle inside itself.
        Rectangle oddDestination, oddClip;
        Check(IconPlacement.TryPlace(farLeft, 0.0135f, 1f, width, height,
                                     canvasTopLeft, canvasSize, out oddDestination, out oddClip),
              "an icon with an offset source is placed");
        Check(IconPlacement.TryClip(oddDestination, offsetSource, oddClip,
                                    out visibleDestination, out visibleSource),
              "an icon with an offset source is clipped");
        Check(visibleSource.X >= offsetSource.X
              && visibleSource.Y >= offsetSource.Y
              && visibleSource.X + visibleSource.Width <= offsetSource.X + offsetSource.Width
              && visibleSource.Y + visibleSource.Height <= offsetSource.Y + offsetSource.Height,
              "the clipped source stays inside its cell, whatever the cell's origin");

        // ---- clipping on the vertical axis is a separate code path ----
        //
        // Everything above cuts horizontally, where the vertical cut is zero — so a copy-paste
        // that read source.X where it means source.Y would pass all of it. This case cuts the
        // top edge only, and asserts the height fraction the same way the width fraction is
        // asserted above. An icon whose top half is above the canvas must lose the top of its
        // source too, or it would be drawn squashed rather than cropped.
        var aboveCanvas = new Vector2(0.5f, canvasTopLeft.Y - 0.002f);
        Rectangle topDestination, topClip;
        Check(IconPlacement.TryPlace(aboveCanvas, 0.0135f, 1f, width, height,
                                     canvasTopLeft, canvasSize, out topDestination, out topClip),
              "an icon straddling the canvas top is still placed");

        Check(IconPlacement.TryClip(topDestination, offsetSource, topClip,
                                    out visibleDestination, out visibleSource),
              "an icon straddling the canvas top is clipped, not dropped");
        Check(visibleDestination.Height < topDestination.Height,
              "the visible part is shorter than the whole icon");
        Check(visibleDestination.Width == topDestination.Width,
              "a vertical cut leaves the width alone");
        Check(visibleSource.Height < offsetSource.Height,
              "the source is cut vertically by the same amount, so the icon does not squash");
        Check(visibleSource.Width == offsetSource.Width,
              "a vertical cut leaves the source width alone");

        float visibleHeightFraction = (float)visibleDestination.Height / topDestination.Height;
        float sourceHeightFraction = (float)visibleSource.Height / offsetSource.Height;
        Check(Math.Abs(visibleHeightFraction - sourceHeightFraction) <= 0.5f / offsetSource.Height + 1e-5f,
              "the visible fraction of the source height equals the visible fraction of the "
              + "destination height (within one texel)");

        // The cut is at the top, so the visible part must start lower than the whole one — this
        // is what distinguishes "cropped from the top" from "shrunk from the middle".
        Check(visibleDestination.Y > topDestination.Y && visibleSource.Y > offsetSource.Y,
              "the surviving part is the bottom of the icon, so both rectangles start lower");

        // ---- entirely outside: nothing is drawn ----
        Rectangle outsideDestination, outsideClip;
        var farAway = new Vector2(2f, 2f);
        Check(!IconPlacement.TryPlace(farAway, 0.0135f, 1f, width, height,
                                      canvasTopLeft, canvasSize, out outsideDestination, out outsideClip),
              "an icon entirely outside the canvas is not placed at all");

        // ---- degenerate inputs never produce a drawable rectangle ----
        Rectangle zeroDestination, zeroClip;
        Check(!IconPlacement.TryPlace(new Vector2(0.5f, 0.5f), 0f, 1f, width, height,
                                      canvasTopLeft, canvasSize, out zeroDestination, out zeroClip),
              "an icon of zero height is not placed");
        Check(!IconPlacement.TryPlace(new Vector2(0.5f, 0.5f), 0.0135f, 1f, 0f, 0f,
                                      canvasTopLeft, canvasSize, out zeroDestination, out zeroClip),
              "an icon is not placed before the client size is known");
        Check(!IconPlacement.TryPlace(new Vector2(0.5f, 0.5f), 0.0001f, 1f, width, height,
                                      canvasTopLeft, canvasSize, out zeroDestination, out zeroClip),
              "an icon smaller than a pixel is not placed");
        Check(!IconPlacement.TryClip(new Rectangle(0, 0, 10, 10), source,
                                     new Rectangle(100, 100, 10, 10),
                                     out visibleDestination, out visibleSource),
              "a clip that misses the icon entirely yields nothing");

        // ---- the layout rectangle becomes window pixels ----
        Rectangle canvasPixels = IconPlacement.ToPixels(canvasTopLeft, canvasSize, width, height);
        Check(canvasPixels.X == (int)Math.Floor(canvasTopLeft.X * width),
              "a layout X becomes a window pixel");
        Check(canvasPixels.Y == (int)Math.Floor(canvasTopLeft.Y * height),
              "a layout Y becomes a window pixel");
        Check(canvasPixels.Width > 0 && canvasPixels.Height > 0,
              "a non-empty layout rectangle becomes a non-empty pixel rectangle");
    }

    // ------------------------------------------------------------------ view state (ADR-0022)
    //
    // The camera, the zoom and the four view toggles are written on the grid and read back on
    // the next open. The round trip is the part that can silently open the schema at the wrong
    // place, and it is the only part that can be exercised without the game.
    private static void ViewStateFormat()
    {
        var camera = new Vector2(-0.26f, 1.75f);

        string text = GridViewFormat.Format(camera, 1.37f, true, false, true, false);

        Vector2 parsedCamera;
        float parsedZoom;
        bool parsedHidden;
        bool parsedSubgrids;
        bool parsedBorders;
        bool parsedConveyors;

        Check(GridViewFormat.TryParse(text, out parsedCamera, out parsedZoom, out parsedHidden,
                                      out parsedSubgrids, out parsedBorders, out parsedConveyors),
              "a formatted view state parses back");
        CheckClose(parsedCamera.X, camera.X, "the camera X of a round trip is unchanged");
        CheckClose(parsedCamera.Y, camera.Y, "the camera Y of a round trip is unchanged");
        CheckClose(parsedZoom, 1.37f, "the zoom of a round trip is unchanged");
        Check(parsedHidden, "the show-invisible flag of a round trip is unchanged");
        Check(!parsedSubgrids, "the show-subgrids flag of a round trip is unchanged");
        Check(parsedBorders, "the grid-borders flag of a round trip is unchanged");
        Check(!parsedConveyors, "the conveyor-network flag of a round trip is unchanged");

        // The same round trip with the flags off, so a formatter that always wrote "1" would be
        // caught: the checks above alone cannot tell "wrote 1" from "always reads 1".
        string off = GridViewFormat.Format(camera, 1f, false, false, false, false);
        Check(GridViewFormat.TryParse(off, out parsedCamera, out parsedZoom, out parsedHidden,
                                      out parsedSubgrids, out parsedBorders, out parsedConveyors),
              "a view state with every flag off parses back");
        Check(!parsedHidden, "the show-invisible flag reads back as off");
        Check(!parsedSubgrids, "the show-subgrids flag reads back as off");
        Check(!parsedBorders, "the grid-borders flag reads back as off");
        Check(!parsedConveyors, "the conveyor-network flag reads back as off");

        // ...and with every flag on, so a formatter that always wrote "0" is caught too.
        string on = GridViewFormat.Format(camera, 1f, true, true, true, true);
        Check(GridViewFormat.TryParse(on, out parsedCamera, out parsedZoom, out parsedHidden,
                                      out parsedSubgrids, out parsedBorders, out parsedConveyors),
              "a view state with every flag on parses back");
        Check(parsedHidden && parsedSubgrids && parsedBorders && parsedConveyors,
              "every flag reads back as on");

        // Invariant culture: on a locale with a decimal comma the format would otherwise contain
        // commas, and the value would be unreadable on the next open.
        Check(text.IndexOf(',') < 0,
              "the view format uses no comma, so a decimal comma locale cannot corrupt it");
        Check(text.Split(' ').Length == GridViewFormat.FieldCount,
              "the view format writes exactly seven space-separated fields");

        // ---- older builds keep their camera ----
        //
        // Forward compatibility by field count rather than by a format version: a world saved
        // before a toggle existed must keep its camera and get that toggle's default. Three
        // fields is the oldest format; four added "show invisible"; six added the two construct
        // options (ADR-0032); seven added the conveyor network (ADR-0040). A missing field takes
        // the default the new build shows.
        Check(GridViewFormat.TryParse("0.5 -0.25 2", out parsedCamera, out parsedZoom, out parsedHidden,
                                      out parsedSubgrids, out parsedBorders, out parsedConveyors),
              "a three-field value from an older build still parses");
        CheckClose(parsedCamera.X, 0.5f, "an older value keeps its camera X");
        CheckClose(parsedZoom, 2f, "an older value keeps its zoom");
        Check(!parsedHidden, "an older value defaults the show-invisible toggle to off");
        Check(parsedSubgrids, "an older value defaults subgrids to shown");
        Check(parsedBorders, "an older value defaults grid borders to drawn");
        Check(parsedConveyors, "an older value defaults the conveyor network to shown");

        Check(GridViewFormat.TryParse("0.5 -0.25 2 1", out parsedCamera, out parsedZoom,
                                      out parsedHidden, out parsedSubgrids, out parsedBorders,
                                      out parsedConveyors),
              "a four-field value from the previous build still parses");
        Check(parsedHidden, "a four-field value keeps its show-invisible flag");
        Check(parsedSubgrids && parsedBorders,
              "a four-field value gets the construct options' defaults");
        Check(parsedConveyors, "a four-field value gets the conveyor default");

        Check(GridViewFormat.TryParse("0.5 -0.25 2 1 0 0", out parsedCamera, out parsedZoom,
                                      out parsedHidden, out parsedSubgrids, out parsedBorders,
                                      out parsedConveyors),
              "a six-field value from the previous build still parses");
        Check(!parsedSubgrids && !parsedBorders, "a six-field value keeps its construct options");
        Check(parsedConveyors, "a six-field value gets the conveyor default");

        // ---- rejections ----
        //
        // Each of these must fall back to the default view rather than producing a garbage camera.
        Check(!GridViewFormat.TryParse(null, out parsedCamera, out parsedZoom, out parsedHidden,
                                       out parsedSubgrids, out parsedBorders, out parsedConveyors),
              "null is not a view state");
        Check(!GridViewFormat.TryParse("", out parsedCamera, out parsedZoom, out parsedHidden,
                                       out parsedSubgrids, out parsedBorders, out parsedConveyors),
              "an empty string is not a view state");
        Check(!GridViewFormat.TryParse("0.5 0.5", out parsedCamera, out parsedZoom, out parsedHidden,
                                       out parsedSubgrids, out parsedBorders, out parsedConveyors),
              "two fields are not a view state: there is no zoom");
        Check(!GridViewFormat.TryParse("0.5 0.5 1 1 1 1 1 1", out parsedCamera, out parsedZoom,
                                       out parsedHidden, out parsedSubgrids, out parsedBorders,
                                       out parsedConveyors),
              "eight fields are not a view state");
        Check(!GridViewFormat.TryParse("0.5  0.5 1 0", out parsedCamera, out parsedZoom,
                                       out parsedHidden, out parsedSubgrids, out parsedBorders,
                                       out parsedConveyors),
              "an empty field is not a view state");
        Check(!GridViewFormat.TryParse("a b c d", out parsedCamera, out parsedZoom, out parsedHidden,
                                       out parsedSubgrids, out parsedBorders, out parsedConveyors),
              "non-numeric fields are not a view state");
        Check(!GridViewFormat.TryParse("0,5 0.5 1 0", out parsedCamera, out parsedZoom,
                                       out parsedHidden, out parsedSubgrids, out parsedBorders,
                                       out parsedConveyors),
              "a decimal comma is not our format");
        Check(!GridViewFormat.TryParse("NaN 0.5 1 0", out parsedCamera, out parsedZoom,
                                       out parsedHidden, out parsedSubgrids, out parsedBorders,
                                       out parsedConveyors),
              "NaN is not a view state");
        Check(!GridViewFormat.TryParse("0.5 Infinity 1 0", out parsedCamera, out parsedZoom,
                                       out parsedHidden, out parsedSubgrids, out parsedBorders,
                                       out parsedConveyors),
              "infinity is not a view state");

        // A zoom of zero or less is refused rather than clamped here: the clamp belongs to the
        // editor's own zoom limits, and a stored zero would divide by zero in ScreenToGraph.
        Check(!GridViewFormat.TryParse("0.5 0.5 0 0", out parsedCamera, out parsedZoom,
                                       out parsedHidden, out parsedSubgrids, out parsedBorders,
                                       out parsedConveyors),
              "a zero zoom is not a view state");
        Check(!GridViewFormat.TryParse("0.5 0.5 -1 0", out parsedCamera, out parsedZoom,
                                       out parsedHidden, out parsedSubgrids, out parsedBorders,
                                       out parsedConveyors),
              "a negative zoom is not a view state");

        // A rejected parse must not leak a half-built camera: the caller writes the out parameters
        // straight into the screen when it succeeds.
        Vector2 rejectedCamera = new Vector2(7f, 7f);
        float rejectedZoom = 7f;
        bool rejectedHidden = true;
        bool rejectedSubgrids = false;
        bool rejectedBorders = false;
        bool rejectedConveyors = false;
        Check(!GridViewFormat.TryParse("bogus", out rejectedCamera, out rejectedZoom,
                                       out rejectedHidden, out rejectedSubgrids, out rejectedBorders,
                                       out rejectedConveyors),
              "a bogus view state is rejected");
        Check(rejectedCamera == Vector2.Zero, "a rejected parse leaves the camera at zero");
        Check(rejectedZoom == 0f, "a rejected parse leaves the zoom at zero");
        Check(!rejectedHidden, "a rejected parse leaves the show-invisible toggle off");
        Check(rejectedSubgrids && rejectedBorders,
              "a rejected parse leaves the construct options at their defaults");
        Check(rejectedConveyors, "a rejected parse leaves the conveyor network at its default");
    }

    // ------------------------------------------------------------------ conveyor network (ADR-0040)
    //
    // The reduction from raw conveyor lines to the links between drawn cards. This is the rule
    // the whole feature rests on: "stop at the next drawn block, walk through everything else".
    // Its failure modes are invisible in a compile — a link to a card that is not there, a
    // missing link through a hidden container, a quadratic explosion on a junction — and all of
    // them are decidable from the lists alone.
    private static void ConveyorNetworkReduction()
    {
        var segments = new List<ConveyorSegment>();
        var visible = new List<long>();
        var links = new List<ConveyorLink>();

        // ---- one line between two drawn blocks ----
        segments.Add(new ConveyorSegment(10, 20));
        visible.Add(10);
        visible.Add(20);
        ConveyorNetwork.Build(segments, visible, links);

        Check(links.Count == 1, "two drawn blocks joined by one line are one link");
        Check(links[0].SourceEntityId == 10 && links[0].TargetEntityId == 20,
              "the link keeps both ends");
        Check(links[0].SameAs(new ConveyorLink(20, 10)),
              "a link is undirected: the same pair written the other way is the same link");

        // ---- a hidden block is walked through, not cut at ----
        //
        // This is the difference between "these two blocks are plumbed together" and "these two
        // blocks are on the same ship". A container with no card sits between them and the game
        // carries items straight through it, so the schema must show one link, not two.
        segments.Clear();
        segments.Add(new ConveyorSegment(10, 99));
        segments.Add(new ConveyorSegment(99, 20));
        ConveyorNetwork.Build(segments, visible, links);
        Check(links.Count == 1, "a hidden block between two drawn ones is walked through");
        Check(links[0].SourceEntityId == 10 && links[0].TargetEntityId == 20,
              "the link spans the hidden block");

        // ---- a drawn junction is a point of intersection, not a pass-through ----
        //
        // The other half of the rule: if the junction has a card, it is the point where the
        // branches meet, and the picture is a star rather than a triangle. This is what makes
        // "the points of intersection are the blocks already on the schema" true in both
        // directions.
        visible.Add(99);
        ConveyorNetwork.Build(segments, visible, links);
        Check(links.Count == 2, "a drawn junction becomes two links, not one");
        Check(FindLink(links, 10, 99) != null, "the junction is linked to the first block");
        Check(FindLink(links, 20, 99) != null, "the junction is linked to the second block");
        Check(FindLink(links, 10, 20) == null,
              "the junction is not skipped: there is no direct link between its neighbours");

        // ---- a hidden hub joins every pair of its drawn leaves ----
        //
        // A junction with no card is invisible, so all of its leaves are adjacent on the
        // schema. Three leaves therefore give three links, not two — the many-to-many the
        // requirement warned about, reduced to the smallest set that still tells the truth.
        segments.Clear();
        segments.Add(new ConveyorSegment(10, 98));
        segments.Add(new ConveyorSegment(98, 20));
        segments.Add(new ConveyorSegment(98, 30));
        visible.Clear();
        visible.Add(10);
        visible.Add(20);
        visible.Add(30);
        ConveyorNetwork.Build(segments, visible, links);
        Check(links.Count == 3, "a hidden hub joins every pair of its drawn leaves");
        Check(FindLink(links, 10, 20) != null, "leaf 1 and leaf 2 are linked");
        Check(FindLink(links, 10, 30) != null, "leaf 1 and leaf 3 are linked");
        Check(FindLink(links, 20, 30) != null, "leaf 2 and leaf 3 are linked");

        // ---- a loop is walked, not hung on ----
        segments.Clear();
        segments.Add(new ConveyorSegment(10, 98));
        segments.Add(new ConveyorSegment(98, 97));
        segments.Add(new ConveyorSegment(97, 20));
        segments.Add(new ConveyorSegment(98, 99));
        segments.Add(new ConveyorSegment(99, 97));
        visible.Clear();
        visible.Add(10);
        visible.Add(20);
        ConveyorNetwork.Build(segments, visible, links);
        Check(links.Count == 1, "a loop between two drawn blocks is one link, not a hang");
        Check(links[0].SourceEntityId == 10 && links[0].TargetEntityId == 20,
              "the loop's link is the pair of drawn ends");

        // ---- a loop back to the source is not a link ----
        segments.Clear();
        segments.Add(new ConveyorSegment(10, 98));
        segments.Add(new ConveyorSegment(98, 10));
        segments.Add(new ConveyorSegment(10, 20));
        ConveyorNetwork.Build(segments, visible, links);
        Check(links.Count == 1, "a loop back to the source produces no self-link");
        Check(FindLink(links, 10, 20) != null, "and the real link is still there");

        // ---- parallel lines and duplicate segments are one link ----
        segments.Clear();
        segments.Add(new ConveyorSegment(10, 20));
        segments.Add(new ConveyorSegment(10, 20));
        segments.Add(new ConveyorSegment(20, 10));
        ConveyorNetwork.Build(segments, visible, links);
        Check(links.Count == 1, "parallel lines between the same pair are one link");

        // ---- only drawn blocks are nodes ----
        segments.Clear();
        segments.Add(new ConveyorSegment(98, 99));
        visible.Clear();
        visible.Add(10);
        visible.Add(20);
        ConveyorNetwork.Build(segments, visible, links);
        Check(links.Count == 0, "a line between two undrawn blocks links nothing");

        // ---- a drawn block with no line at all ----
        segments.Clear();
        segments.Add(new ConveyorSegment(10, 20));
        visible.Add(30);
        ConveyorNetwork.Build(segments, visible, links);
        Check(links.Count == 1, "a drawn block outside the network does not disturb the link");
        Check(FindLink(links, 10, 20) != null, "and the link is the pair that exists");

        // ---- degenerate inputs ----
        Check(ConveyorNetwork.Build(null, visible, links) == 0 && links.Count == 0,
              "null segments clear the link list");
        Check(ConveyorNetwork.Build(segments, null, links) == 0 && links.Count == 0,
              "null visible ids clear the link list");
        Check(ConveyorNetwork.Build(segments, new List<long>(), links) == 0 && links.Count == 0,
              "fewer than two drawn blocks cannot produce a link");
        Check(ConveyorNetwork.Build(segments, visible, null) == 0,
              "a null output list is refused, not thrown at");

        // ---- determinism ----
        //
        // The list is rebuilt on every structural refresh and read by the draw path, so the same
        // ship has to produce the same order. This is the assertion that keeps the picture from
        // reshuffling between two passes.
        var first = new List<ConveyorLink>();
        var second = new List<ConveyorLink>();
        segments.Clear();
        segments.Add(new ConveyorSegment(30, 98));
        segments.Add(new ConveyorSegment(98, 10));
        segments.Add(new ConveyorSegment(98, 20));
        visible.Clear();
        visible.Add(10);
        visible.Add(20);
        visible.Add(30);
        ConveyorNetwork.Build(segments, visible, first);
        ConveyorNetwork.Build(segments, visible, second);
        Check(first.Count == second.Count, "the same input produces the same number of links");
        for (int i = 0; i < first.Count; i++)
        {
            Check(first[i].SameAs(second[i]), "link " + i + " is produced in the same order");
        }

        // ---- the graph's own side of the contract ----
        var graph = new GridGraph(1);
        graph.AddNode(MakeTarget(10, "LargeBlockConveyor"));
        graph.AddNode(MakeTarget(20, "LargeBlockConveyor"));
        graph.AddConveyor(new ConveyorLink(10, 20));
        Check(graph.ConveyorCount == 1, "a conveyor link is stored on the graph");
        Check(graph.Conveyors[0].SourceEntityId == 10, "and keeps its normalised ends");

        graph.AddConveyor(new ConveyorLink(10, 10));
        graph.AddConveyor(new ConveyorLink(0, 20));
        graph.AddConveyor(null);
        Check(graph.ConveyorCount == 1, "a self-link, a zero id and a null are refused");

        graph.AddNode(MakeTarget(30, "LargeBlockConveyor"));
        graph.AddConveyor(new ConveyorLink(20, 30));
        Check(graph.ConveyorCount == 2, "a second link is stored");
        graph.RemoveNode(20);
        Check(graph.ConveyorCount == 0,
              "removing a node drops every conveyor link that touched it");
        graph.Clear();
        Check(graph.ConveyorCount == 0, "clearing the graph drops its conveyor links");
    }

    /// <summary>
    /// A link between two ids, in either order, or null. Used by the checks above so a failure
    /// names the pair rather than an index.
    /// </summary>
    private static ConveyorLink FindLink(List<ConveyorLink> links, long a, long b)
    {
        for (int i = 0; i < links.Count; i++)
        {
            if (links[i].SameAs(new ConveyorLink(a, b))) return links[i];
        }
        return null;
    }

    // ------------------------------------------------------------------ conveyor pipes (ADR-0040)
    //
    // The pipe's two rails and the staircase that rounds a bend. The draw path cannot be asked
    // about any of this: a wrong radius is a picture that looks almost right, and a missing rail
    // is a pipe that reads as a wire.
    private static void ConveyorPipeRails()
    {
        var route = new List<Vector4>();
        var pipes = new List<Vector4>();
        var router = new PipeRouter();

        const float half = 0.5f;      // half the pipe's width, in the route's units
        const float corner = 6f;      // bend radius, in the same units

        // ---- a straight run is two parallel rails and nothing else ----
        route.Add(new Vector4(0f, 10f, 100f, 10f));
        int count = router.Build(route, corner, half, 1f, half, pipes);

        Check(count == 2, "a straight pipe has two rails (got " + count + ")");
        CheckAllAxisAligned(pipes, "straight pipe");
        Check(FindRail(pipes, true, 10f - half) != null, "the lower rail sits half a width below");
        Check(FindRail(pipes, true, 10f + half) != null, "the upper rail sits half a width above");
        Check(FindRail(pipes, true, 10f) == null, "there is no rail on the centre line");
        Check(PipeRailSpan(pipes, true, 10f - half) >= 99.9f,
              "the rail spans the whole straight run");

        // ---- a bend is rounded, not a right angle ----
        //
        // The two rails stop at the fillet's tangent points and the corner itself is bridged by
        // a staircase of short axis-aligned pieces. The assertion that matters is the one a
        // sharp corner would fail: the horizontal rail stops `corner` short of the vertex.
        route.Clear();
        route.Add(new Vector4(0f, 0f, 100f, 0f));
        route.Add(new Vector4(100f, 0f, 100f, 100f));
        count = router.Build(route, corner, half, 1f, half, pipes);

        Check(count > 4, "a bend adds staircase pieces to the two rails (got " + count + ")");
        CheckAllAxisAligned(pipes, "bent pipe");

        // The rail that runs into the bend stops well before the vertex: a sharp join would
        // carry it all the way to x = 100 and drop from there.
        Check(PipeRailSpan(pipes, true, -half) < 100f - 1f,
              "the incoming rail stops short of the vertex: the bend is rounded");
        Check(PipeRailSpan(pipes, true, half) < 100f - 1f,
              "the inner incoming rail stops short of the vertex too");

        // No rail piece passes through the sharp corner: every piece of the bend is offset from
        // the vertex by at least half the pipe's width. A square join would have pieces ending
        // exactly at (100, 0) on both axes.
        bool touchesVertex = false;
        for (int i = 0; i < pipes.Count; i++)
        {
            if (PipePassesThrough(pipes[i], 100f, 0f, 1e-4f)) touchesVertex = true;
        }
        Check(!touchesVertex, "no rail passes through the sharp corner: the bend is rounded");

        // ---- the staircase follows the fillet's own arc ----
        //
        // The fillet's centre is `corner` from each leg, so the arc's 45° point is at a known
        // distance from the vertex. The staircase samples that point exactly (it is the step
        // boundary), and asserting it pins the centre, both radii and the sampling in one
        // comparison — a staircase built from the wrong centre or with the wrong radius misses
        // it by whole units.
        const float diagonal = 0.70710678f;
        float outerMidX = 100f - corner + (corner + half) * diagonal;
        float outerMidY = corner - (corner + half) * diagonal;
        float innerMidX = 100f - corner + (corner - half) * diagonal;
        float innerMidY = corner - (corner - half) * diagonal;

        Check(FindRailPoint(pipes, outerMidX, outerMidY),
              "the outer rail passes the 45° point of its arc");
        Check(FindRailPoint(pipes, innerMidX, innerMidY),
              "the inner rail passes the 45° point of its arc");

        // ---- a very short leg clamps the fillet ----
        //
        // Two bends cannot overlap: the radius may use at most half of each adjacent leg. A
        // route that ignored this would have its rails cross themselves at the corner.
        route.Clear();
        route.Add(new Vector4(0f, 0f, 2f, 0f));
        route.Add(new Vector4(2f, 0f, 2f, 100f));
        count = router.Build(route, corner, half, 1f, half, pipes);
        Check(count > 0, "a short leg still produces a pipe");
        CheckAllAxisAligned(pipes, "clamped pipe");
        Check(AllFinite(pipes), "the clamped fillet produces no NaN or infinity");

        // The fillet may use at most half of the short leg, so the bend reaches at most one
        // radius plus half a width past the vertex. An unclamped radius of six would reach
        // 2 + 6.5 and the pipe would loop back over its own straight.
        float maxX = 0f;
        for (int i = 0; i < pipes.Count; i++)
        {
            if (pipes[i].X > maxX) maxX = pipes[i].X;
            if (pipes[i].Z > maxX) maxX = pipes[i].Z;
        }
        Check(maxX <= 2f + 1f + half + 1e-3f,
              "the clamped fillet uses at most half of the short leg (max x " + maxX + ")");

        // ---- the aspect ratio widens only the horizontal extents ----
        //
        // One X unit of the layout is the window's width and one Y unit its height, so a pipe
        // must be the same width on screen whatever the window shape. A horizontal rail's offset
        // is vertical and needs no correction; a vertical rail's offset is horizontal and does.
        route.Clear();
        route.Add(new Vector4(0f, 0f, 100f, 0f));
        route.Add(new Vector4(100f, 0f, 100f, 100f));
        const float wide = 0.5f;
        router.Build(route, corner, half, wide, half, pipes);
        Check(FindRail(pipes, true, -half) != null,
              "a horizontal rail keeps its vertical offset at a wide aspect");
        Check(FindRail(pipes, false, 100f + half * wide) != null,
              "a vertical rail's horizontal offset is scaled by the aspect");
        Check(FindRail(pipes, false, 100f + half) == null,
              "and it is not the uncorrected offset");

        // ---- degenerate inputs ----
        Check(router.Build(null, corner, half, 1f, half, pipes) == 0 && pipes.Count == 0,
              "a null route clears the rail list");
        Check(router.Build(new List<Vector4>(), corner, half, 1f, half, pipes) == 0,
              "an empty route produces no rails");
        Check(router.Build(route, corner, 0f, 1f, half, pipes) == 0,
              "a zero width produces no rails");
        Check(router.Build(route, corner, half, 0f, half, pipes) > 0,
              "an unknown aspect degrades to square units instead of producing nothing");
        Check(router.Build(route, corner, half, 1f, 0f, pipes) > 0,
              "an unknown step length still produces a bend");

        // The buffer is reused: a build must replace its contents, not append to them. Both
        // builds use the same arguments, so a difference could only come from accumulation.
        router.Build(route, corner, half, 1f, half, pipes);
        int before = pipes.Count;
        router.Build(route, corner, half, 1f, half, pipes);
        Check(pipes.Count == before, "the rail buffer is cleared on each build");

        // The router's own scratch survives a long route and a short one in either order: a
        // build must not depend on what the previous build left behind.
        var longRoute = new List<Vector4>();
        for (int i = 0; i < 12; i++)
        {
            longRoute.Add(i % 2 == 0
                ? new Vector4(0f, i * 10f, 100f, i * 10f)
                : new Vector4(100f, i * 10f, 100f, (i + 1) * 10f));
        }
        var longPipes = new List<Vector4>();
        int longCount = router.Build(longRoute, corner, half, 1f, half, longPipes);
        Check(longCount > 0, "a long route with many bends builds");

        var shortRoute = new List<Vector4> { new Vector4(0f, 0f, 50f, 0f) };
        var shortPipes = new List<Vector4>();
        Check(router.Build(shortRoute, corner, half, 1f, half, shortPipes) == 2,
              "a short route after a long one still builds exactly two rails");
    }

    /// <summary>
    /// The rail at a given offset, or null. <paramref name="horizontal"/> selects the direction
    /// the rail runs in; <paramref name="offset"/> is the coordinate it sits at (y for a
    /// horizontal rail, x for a vertical one). Tolerance is a thousandth of a unit, which is far
    /// below a pixel in the layout's units.
    /// </summary>
    private static Vector4? FindRail(List<Vector4> pipes, bool horizontal, float offset)
    {
        for (int i = 0; i < pipes.Count; i++)
        {
            Vector4 piece = pipes[i];
            bool isHorizontal = Math.Abs(piece.Y - piece.W) < 1e-6f;
            bool isVertical = Math.Abs(piece.X - piece.Z) < 1e-6f;

            if (horizontal && isHorizontal && Math.Abs(piece.Y - offset) < 1e-3f) return piece;
            if (!horizontal && isVertical && Math.Abs(piece.X - offset) < 1e-3f) return piece;
        }
        return null;
    }

    /// <summary>
    /// How far the rail at a given offset reaches along its own axis, from the origin: the
    /// largest coordinate of every piece that belongs to it. Used to assert where a rail stops
    /// at a fillet, which is the visible difference between a rounded bend and a sharp one.
    /// </summary>
    private static float PipeRailSpan(List<Vector4> pipes, bool horizontal, float offset)
    {
        float reach = 0f;

        for (int i = 0; i < pipes.Count; i++)
        {
            Vector4 piece = pipes[i];
            bool isHorizontal = Math.Abs(piece.Y - piece.W) < 1e-6f;
            bool isVertical = Math.Abs(piece.X - piece.Z) < 1e-6f;

            if (horizontal && isHorizontal && Math.Abs(piece.Y - offset) < 1e-3f)
            {
                if (piece.Z > reach) reach = piece.Z;
                if (piece.X > reach) reach = piece.X;
            }
            if (!horizontal && isVertical && Math.Abs(piece.X - offset) < 1e-3f)
            {
                if (piece.W > reach) reach = piece.W;
                if (piece.Y > reach) reach = piece.Y;
            }
        }
        return reach;
    }

    /// <summary>
    /// True when some rail piece has an endpoint at the given point. The staircase's sample
    /// points are exactly the endpoints it produces, so this is how the checks pin the fillet's
    /// geometry.
    /// </summary>
    private static bool FindRailPoint(List<Vector4> pipes, float x, float y)
    {
        const float tolerance = 1e-3f;

        for (int i = 0; i < pipes.Count; i++)
        {
            Vector4 piece = pipes[i];
            if (Math.Abs(piece.X - x) < tolerance && Math.Abs(piece.Y - y) < tolerance) return true;
            if (Math.Abs(piece.Z - x) < tolerance && Math.Abs(piece.W - y) < tolerance) return true;
        }
        return false;
    }

    private static bool PipePassesThrough(Vector4 piece, float x, float y, float tolerance)    {
        float minX = Math.Min(piece.X, piece.Z) - tolerance;
        float maxX = Math.Max(piece.X, piece.Z) + tolerance;
        float minY = Math.Min(piece.Y, piece.W) - tolerance;
        float maxY = Math.Max(piece.Y, piece.W) + tolerance;

        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    private static bool AllFinite(List<Vector4> pieces)
    {
        for (int i = 0; i < pieces.Count; i++)
        {
            Vector4 p = pieces[i];
            if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z) || float.IsNaN(p.W)) return false;
            if (float.IsInfinity(p.X) || float.IsInfinity(p.Y)
                || float.IsInfinity(p.Z) || float.IsInfinity(p.W)) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ block search (ADR-0023)
    //
    // The rule is the game's terminal filter: every space-separated term must be a substring of
    // the block's name or of its type, case-insensitively. A second, subtly different rule would
    // teach the player two behaviours for one gesture.
    private static void BlockSearchRule()
    {
        var terms = new List<string>();

        // ---- an empty query matches everything ----
        BlockSearch.SplitTerms("", terms);
        Check(terms.Count == 0, "an empty query has no terms");
        Check(BlockSearch.Matches(terms, "Timer", "Timer Block"),
              "an empty query matches every block");
        BlockSearch.SplitTerms("   ", terms);
        Check(terms.Count == 0, "a query of spaces has no terms either");
        Check(BlockSearch.Matches(terms, "Timer", "Timer Block"),
              "a query of spaces matches every block");
        BlockSearch.SplitTerms(null, terms);
        Check(terms.Count == 0, "a null query has no terms and does not crash");

        // ---- one term, substring, either field ----
        BlockSearch.SplitTerms("tim", terms);
        Check(terms.Count == 1 && terms[0] == "tim", "a query is split into its terms");
        Check(BlockSearch.Matches(terms, "Timer 1", "Timer Block"),
              "a term matches the block's name");
        Check(BlockSearch.Matches(terms, "Startup", "Timer Block"),
              "a term matches the block's displayed type");
        Check(!BlockSearch.Matches(terms, "Sensor", "Sensor Block"),
              "a term that is in neither field does not match");

        // Case-insensitivity is the game's behaviour and must survive the split. Terms are stored
        // as typed, so this also pins that the comparison, not the storage, folds case.
        BlockSearch.SplitTerms("TIMER", terms);
        Check(terms.Count == 1 && terms[0] == "TIMER",
              "terms are stored as typed, so case is folded once, at comparison");
        Check(BlockSearch.Matches(terms, "my timer", "Timer Block"),
              "matching is case-insensitive");

        // The displayed type is what the terminal searches — not the engine subtype. A query in
        // the player's language must match the type even when the subtype shares no characters
        // with it, which is the whole reason Node carries TypeDisplayName (ADR-0023).
        // The name deliberately contains none of the query, so only the type field can match.
        BlockSearch.SplitTerms("таймер", terms);
        Check(BlockSearch.Matches(terms, "Блок 1", "Таймер"),
              "a query in the player's language matches the localized type");
        Check(!BlockSearch.Matches(terms, "Блок 1", "LargeBlockTimer"),
              "the engine subtype is not what a localized query is matched against");

        // ---- several terms are ANDed, not ORed ----
        //
        // This is the half of the rule that is easy to get wrong: the game hides a block unless
        // *every* term is found. An OR would leave a block visible on a term it does not contain.
        BlockSearch.SplitTerms("timer block", terms);
        Check(terms.Count == 2, "a query with a space has two terms");
        Check(BlockSearch.Matches(terms, "Timer 1", "Timer Block"),
              "every term may match a different field");
        Check(!BlockSearch.Matches(terms, "Timer 1", "Small Timer"),
              "one unmatched term is enough to reject the block");

        // ---- a block with no name at all ----
        //
        // A block whose name could not be read falls back to its subtype, and the search must not
        // throw on a null name.
        BlockSearch.SplitTerms("timer", terms);
        Check(BlockSearch.Matches(terms, null, "Timer Block"),
              "a null name is searched by type alone");
        Check(!BlockSearch.Matches(terms, null, null),
              "a block with neither name nor type matches nothing");

        // ---- spaces collapse ----
        //
        // The game splits on every space, and an empty term is a substring of everything, so a
        // double space must not reject every block.
        BlockSearch.SplitTerms("  timer  ", terms);
        Check(terms.Count == 1 && terms[0] == "timer",
              "runs of spaces produce no empty terms");
        Check(BlockSearch.Matches(terms, "Timer 1", "Timer Block"),
              "a query with extra spaces still matches");
    }

    // ------------------------------------------------------------------ categories (ADR-0030)
    //
    // Classification is a heuristic on the subtype, and its whole risk is *order*: several real
    // subtype names contain more than one family word, and a reordering that looks like tidying
    // silently reclassifies blocks. Nothing here is visible in a compile, and the symptom in the
    // game would be "the cockpit lost its TargetLocked port" — which reads as a port bug, not as
    // a classification bug. So the traps are asserted by name.
    private static void BlockCategories()
    {
        // ---- the traps: the more specific token must win ----
        Check(BlockPortCatalog.ResolveCategory("LargeBlockEventController") == BlockCategory.EventController,
              "an event controller is not classified as a cockpit");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockButtonPanel") == BlockCategory.ButtonPanel,
              "a button panel is not classified as a light");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockSolarPanel") == BlockCategory.SolarPanel,
              "a solar panel is not classified as a light");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockAirVent") == BlockCategory.AirVent,
              "an air vent is not classified as an oxygen block");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockHydrogenThruster") == BlockCategory.Hydrogen,
              "a hydrogen thruster is not classified as a plain thruster");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockLCDPanel") == BlockCategory.Light,
              "an LCD panel is a light");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockCockpitIndustrial") == BlockCategory.Cockpit,
              "an industrial cockpit is a cockpit");
        Check(BlockPortCatalog.ResolveCategory("SmallBlockCockpit") == BlockCategory.Cockpit,
              "a small cockpit is a cockpit");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockSmallCargoContainer") == BlockCategory.CargoContainer,
              "a cargo container is a cargo container");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockLandingGear") == BlockCategory.LandingGear,
              "landing gear is not classified as a light");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockProgrammable") == BlockCategory.Programmable,
              "a programmable block is a programmable block");

        // ---- the ordinary ones: one check per member that has a curated event ----
        Check(BlockPortCatalog.ResolveCategory("LargeBlockTimerBlock") == BlockCategory.Timer, "timer");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockSensor") == BlockCategory.Sensor, "sensor");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockWarhead") == BlockCategory.Warhead, "warhead");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockDoor") == BlockCategory.Door, "door");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockConnector") == BlockCategory.Connector, "connector");
        Check(BlockPortCatalog.ResolveCategory("SafeZoneBlock") == BlockCategory.SafeZone, "safe zone");

        // ---- degradation: an unknown subtype is a legal value, not an error ----
        Check(BlockPortCatalog.ResolveCategory("LargeBlockConveyor") == BlockCategory.Unknown,
              "a block the table does not know is Unknown");
        Check(BlockPortCatalog.ResolveCategory("LargeBlockCamera") == BlockCategory.Unknown,
              "a camera is Unknown — the table has no category for it");
        Check(BlockPortCatalog.ResolveCategory("") == BlockCategory.Unknown,
              "an empty subtype is Unknown");
        Check(BlockPortCatalog.ResolveCategory(null) == BlockCategory.Unknown,
              "a null subtype is Unknown");

        // ---- every enum member has a descriptor, and a descriptor is never null ----
        foreach (BlockCategory category in Enum.GetValues(typeof(BlockCategory)))
        {
            Check(BlockPortCatalog.Descriptor(category) != null,
                  "descriptor for " + category + " is not null");
        }

        // The unknown descriptor is shared and empty: no events of its own, no metrics.
        Check(!BlockPortCatalog.Descriptor(BlockCategory.Unknown).HasEvents,
              "the unknown category has no curated events of its own");
        Check(BlockPortCatalog.Descriptor(BlockCategory.Unknown).MetricCount == 0,
              "the unknown category publishes no metrics");

        // ---- the curated events did not change with the refactor ----
        Check(EventsOf(BlockCategory.Cockpit, "TargetLocked"),
              "a cockpit fires TargetLocked");
        Check(EventsOf(BlockCategory.Cockpit, "TargetLost"), "a cockpit fires TargetLost");
        Check(EventsOf(BlockCategory.Timer, "Triggered"), "a timer fires Triggered");
        Check(EventsOf(BlockCategory.Sensor, "Detected"), "a sensor fires Detected");
        Check(EventsOf(BlockCategory.EventController, "EventTriggered"),
              "an event controller fires EventTriggered");
        Check(EventsOf(BlockCategory.ButtonPanel, "ButtonPressed"),
              "a button panel fires ButtonPressed");
        Check(EventsOf(BlockCategory.AirVent, "Depressurized"), "an air vent fires Depressurized");
        Check(EventsOf(BlockCategory.AirVent, "Pressurized"), "an air vent fires Pressurized");
        Check(EventsOf(BlockCategory.Warhead, "Detonated"), "a warhead fires Detonated");
        Check(EventsOf(BlockCategory.Door, "OpenChanged"), "a door fires OpenChanged");
        Check(EventsOf(BlockCategory.LandingGear, "LockedChanged"),
              "landing gear fires LockedChanged");
        Check(EventsOf(BlockCategory.Connector, "LockedChanged"), "a connector fires LockedChanged");
        Check(EventsOf(BlockCategory.SafeZone, "Entered"), "a safe zone fires Entered");
        Check(EventsOf(BlockCategory.SafeZone, "Left"), "a safe zone fires Left");

        // A category with no curated event gets exactly one generic port, on both toolbar shapes.
        Check(PortsOf(MakeNode(900, "LargeBlockConveyor", TimerToolbar(), "OnOff")).Count == 1,
              "an unknown category with a single-group toolbar gets one generic port");
        Check(PortsOf(MakeNode(901, "LargeBlockConveyor", SensorToolbar(), "OnOff")).Count == 2,
              "an unknown category with a yes/no toolbar gets the generic port in both groups");

        // ---- the node is classified once, and its state is sized by the descriptor ----
        Node cockpit = MakeNode(910, "LargeBlockCockpitIndustrial", TimerToolbar(), "OnOff");
        Check(cockpit.Category == BlockCategory.Cockpit, "a node carries its category");
        Check(cockpit.State.MetricCount == BlockPortCatalog.Descriptor(cockpit.Category).MetricCount,
              "a node's state is sized by its category's descriptor");

        // A group has no subtype, so it is Unknown and carries no metrics either.
        var group = new Node("group K", "group K");
        Check(group.Category == BlockCategory.Unknown, "a group has no category");
        Check(group.State.MetricCount == 0, "a group's state has no metrics");
    }

    /// <summary>True when the category's descriptor lists this event.</summary>
    private static bool EventsOf(BlockCategory category, string key)
    {
        string[] events = BlockPortCatalog.Descriptor(category).Events;
        for (int i = 0; i < events.Length; i++)
        {
            if (string.Equals(events[i], key, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>The event ports a node ends up with.</summary>
    private static IList<Port> PortsOf(Node node)
    {
        return node.Events;
    }

    // ------------------------------------------------------------------ controller state (ADR-0030)
    //
    // The point of moving the controller's fields off Node was that *having controller state is
    // being a controller*. That makes one invariant worth asserting: the reference and the flag
    // can never disagree, because there is only one of them. Everything else here is the old
    // watch behaviour re-asserted through its new home, so the move cannot have changed it.
    private static void ControllerSpecificity()
    {
        // ---- the reference is the flag ----
        Node plain = MakeTarget(700, "LargeBlockConveyor", "OnOff");
        Check(plain.Controller == null, "an ordinary block carries no controller state");
        Check(!plain.IsEventController, "and it is not an event controller");

        Node controller = new Node(701, "EventControllerLarge", "controller", Vector3I.Zero,
                                   SensorToolbar(), true);
        Check(controller.Controller != null, "a controller carries controller state");
        Check(controller.IsEventController, "and the flag agrees — they are one fact");
        Check(controller.Controller.Watched.Count == 0, "a fresh controller watches nothing");

        var group = new Node("group C", "group C");
        Check(group.Controller == null, "a group is never a controller");
        Check(!group.IsEventController, "and its flag agrees");

        // ---- the watch methods refuse a node that is not a controller ----
        var graph = new GridGraph(31);
        graph.AddNode(plain);
        graph.AddNode(controller);
        Check(!graph.TryAddWatch(plain, 700, "self"), "a plain block cannot watch anything");
        Check(!graph.HasWatch(plain, 700), "and it reports watching nothing");
        Check(!graph.RemoveWatch(plain, 700), "removing from a plain block does nothing");

        var ids = new List<long>();
        GridGraph.CollectWatchedIds(plain, ids);
        Check(ids.Count == 0, "a plain block collects no watched ids");

        // SyncWatched on a non-controller is a no-op rather than a crash: the refresh pass calls
        // it for whatever node it is updating, and an exception there would reach the game loop.
        GridGraph.SyncWatched(plain, new List<long> { 1, 2 }, id => "x");
        Check(plain.Controller == null, "SyncWatched does not create controller state");

        // ---- the behaviour through the new home ----
        Check(graph.TryAddWatch(controller, 700, plain.DisplayName), "a controller can watch a block");
        Check(controller.Controller.Watched.Count == 1, "the entry is in the controller's own list");
        Check(graph.HasWatch(controller, 700), "the graph agrees it is watched");
        Check(controller.Controller.FindWatch(700) != null, "the entry is findable by id");
        Check(!graph.TryAddWatch(controller, 700, "again"), "the same block is not watched twice");

        GridGraph.CollectWatchedIds(controller, ids);
        Check(ids.Count == 1 && ids[0] == 700, "the watched id is collected from the new home");

        // The cap still lives on the controller state, and it is the same cap as any other stack.
        Check(controller.Controller.IsFull == false, "one entry does not fill the stack");
        for (int i = 0; i < GridGraph.MaxPortsPerSide - 1; i++)
        {
            graph.TryAddWatch(controller, 800 + i, "b" + i);
        }
        Check(controller.Controller.IsFull, "the stack reports full at the cap");
        Check(!graph.TryAddWatch(controller, 999, "overflow"), "and refuses one more");

        // ---- the third line of the card follows the same reference ----
        CheckClose(PortGeometry.CardHeight(controller),
                   VgsConstants.NodeHeight + VgsConstants.NodeStatusLineHeight,
                   "a controller's card is a line taller");
        CheckClose(PortGeometry.CardHeight(plain), VgsConstants.NodeHeight,
                   "an ordinary card is not");
        Check(controller.Controller.StatusText == null,
              "a fresh controller has no description until the refresh pass writes one");
        controller.Controller.StatusText = "Condition is True";
        CheckClose(PortGeometry.CardHeight(controller),
                   VgsConstants.NodeHeight + VgsConstants.NodeStatusLineHeight,
                   "the extra line does not appear and disappear with the text");
    }

    // ------------------------------------------------------------------ block state (Step 2)
    //
    // The state pass exists so the schema can look live without a structural rebuild: a card's
    // damage is read twice a second and written into the node, while ports, groups, toolbars and
    // edges are left exactly as the structural pass left them. Two things can go wrong there, and
    // both are invisible until the game is running:
    //
    //   * the write touches more than the state, and the picture is rebuilt under the player's
    //     pointer twice a second (ports lose their slots, the group frame jumps);
    //   * the value is not clamped, and a card draws its damage indicator outside the card.
    //
    // Everything below is plain data, so it is asserted here rather than by a game launch.
    // ------------------------------------------------------------------ power switch (ADR-0035)
    //
    // The card's left column holds the block's on/off switch. Four things about it can be wrong in
    // ways that only the game would show, and all four are arithmetic or a rule:
    //
    //  * the slot is a fraction of the *card*, so it has to split a controller's taller card the
    //    same way it splits a standard one — otherwise the switch floats on exactly the cards that
    //    are the tallest;
    //  * the icon is wider than it is tall, so a caller that reserved the sprite's square box would
    //    reserve twice the room it needs, and one that assumed a square would let the ink run into
    //    the block's name;
    //  * the block's name starts to the right of the column, not under it;
    //  * "too damaged to switch on" is not "off", and the state has to say which.
    private static void PowerSwitch()
    {
        // A node built here has CanBeEnabled left at its default (false), which is what every
        // existing card in the other checks is. It is set explicitly below, the way
        // GridGraphBuilder sets it from the block's type.
        Node block = MakeTarget(20, "LargeBlockConveyor", "OnOff");
        block.Position = new Vector2(0.30f, 0.10f);
        block.CanBeEnabled = true;

        const float aspect = 0.5625f;   // 16:9, the shape the editor is normally used in

        // ---- the column exists only where there is a switch ----
        Check(PortGeometry.HasPowerSwitch(block), "a functional block carries a power switch");

        Node noSwitch = MakeTarget(21, "LargeBlockConveyor", "OnOff");
        Check(!PortGeometry.HasPowerSwitch(noSwitch),
              "a block the game does not report as functional carries no switch");

        var group = new Node("group P", "group P");
        Check(!PortGeometry.HasPowerSwitch(group), "a group frame has no power switch");

        Vector2 unusedTopLeft, unusedSize;
        Check(!PortGeometry.TryPowerSwitchRect(noSwitch, aspect, out unusedTopLeft, out unusedSize),
              "a block without a switch has no switch rectangle");
        Check(!PortGeometry.TryPowerSwitchRect(group, aspect, out unusedTopLeft, out unusedSize),
              "a group has no switch rectangle");
        Check(!PortGeometry.TryPowerSwitchRect(null, aspect, out unusedTopLeft, out unusedSize),
              "a null node has no switch rectangle");

        // ---- the column is inset from the card on all four sides ----
        //
        // The column's inner box is the card less the vertical padding above and below, and the
        // two bands split that box. The horizontal inset is the same margin the button row keeps
        // from the trailing edge. Both are asserted here because "the icon lies on the frame" is
        // exactly the defect the padding was added for, and it is invisible in a compile.
        Vector2 cardTopLeft, cardSize;
        PortGeometry.CardRect(block, out cardTopLeft, out cardSize);

        Vector2 switchTopLeft, switchSize;
        Check(PortGeometry.TryPowerSwitchRect(block, aspect, out switchTopLeft, out switchSize),
              "a functional block has a switch rectangle");

        CheckClose(VgsConstants.PowerSwitchPaddingPixels, 2f,
                   "the column's vertical padding is the two conditional pixels the requirement names");

        float inner = cardSize.Y - 2f * VgsConstants.PowerSwitchPadding;
        CheckClose(PortGeometry.PowerSwitchContentHeight(block), inner,
                   "the column's inner height is the card's height less the padding at both ends");

        // Top gap, bottom gap, and the switch inside them.
        CheckClose(switchTopLeft.Y - cardTopLeft.Y, VgsConstants.PowerSwitchPadding,
                   "the switch keeps the padding from the card's top edge — it no longer lies on it");
        Check(switchTopLeft.Y - cardTopLeft.Y > 0f,
              "the switch is strictly below the card's top edge");

        float bottomGap = cardTopLeft.Y + cardSize.Y
                          - (switchTopLeft.Y + inner);
        CheckClose(bottomGap, VgsConstants.PowerSwitchPadding,
                   "the lower band keeps the same padding from the card's bottom edge");

        CheckClose(switchSize.Y + (inner - switchSize.Y) + 2f * VgsConstants.PowerSwitchPadding,
                   cardSize.Y,
                   "the two bands and the two paddings add up to the card's height, with nothing left over");

        // ---- the upper band is 40% of the column's inner height ----
        CheckClose(switchSize.Y, inner * VgsConstants.PowerSwitchBandRatio,
                   "the switch's ink fills the upper band of the column's inner box");
        CheckClose(switchSize.Y, inner * 0.40f,
                   "that band is exactly the 40% the requirement names");

        // The ratio itself is a decision the requirement fixed, so it is pinned as a literal rather
        // than only used: a test that derives its expected value from the constant it is checking
        // can never fail, and this one exists precisely to catch a change of that constant.
        CheckClose(VgsConstants.PowerSwitchBandRatio, 0.40f,
                   "the upper band is 40% of the column's inner height, as the requirement states");

        // ---- the width comes from the icon, and the icon is wider than tall ----
        //
        // The atlas build measures the toggle's ink at 576 × 384, an aspect of 1.5, so the band is
        // 1.5 × its own height wide *on screen*. The rectangle is in layout units, whose X and Y do
        // not share a scale (ADR-0009), so the comparison is made in the vertical unit: a width in
        // X units divided by the client aspect is the same length measured the way heights are.
        float inkAspect = PortGeometry.PowerSwitchInkAspect;
        CheckClose(inkAspect, 1.5f, "the toggle icon's ink is one and a half times as wide as tall");
        CheckClose(switchSize.X / aspect / switchSize.Y, inkAspect,
                   "the switch is as wide on screen as the ink's own aspect says");
        CheckClose(switchSize.X / aspect, inner * 0.60f,
                   "the switch is 60% of the column's inner height wide");

        // The padding costs the switch size, and the number is pinned rather than left implicit:
        // the width is now 60% of the *inner* height, so it is a little under the row button's
        // diameter (60% of the card's height) instead of exactly equal to it, as ADR-0035 first
        // recorded. If the size is ever bought back, this is the assertion that has to change.
        Check(switchSize.X / aspect < cardSize.Y * 0.60f,
              "the switch is narrower than a row button's diameter, because the column is padded");
        CheckClose((switchSize.X / aspect) / (cardSize.Y * 0.60f),
                   1f - 2f * VgsConstants.PowerSwitchPadding / cardSize.Y,
                   "and it is short by exactly the share the two paddings take from the card");

        // The two states are one control and must not change size: the column's width is a
        // property of the block's type, and a switch that grew when it lit up would move the name.
        CheckClose(VgsIconAtlas.InkAspect(VgsIcon.ToggleOn), VgsIconAtlas.InkAspect(VgsIcon.ToggleOff),
                   "the on and off icons have the same ink aspect, so the column cannot change width");

        // ---- the switch is inset from the card, not on its border ----
        CheckClose(switchTopLeft.X - cardTopLeft.X, VgsConstants.CardButtonMargin * aspect,
                   "the switch keeps the same margin from the card's left border as a button does "
                   + "from the right one");
        Check(switchTopLeft.X >= cardTopLeft.X - 1e-6f, "the switch never starts left of the card");
        Check(switchTopLeft.X + switchSize.X <= cardTopLeft.X + cardSize.X + 1e-6f,
              "the switch stays inside the card's width");
        Check(switchTopLeft.Y + switchSize.Y <= cardTopLeft.Y + cardSize.Y - VgsConstants.PowerSwitchPadding
              + 1e-6f,
              "the switch stays inside the card's height, with the lower band and both paddings below it");

        // The lower band is what is left of the inner box, and it is the majority of it: nothing is
        // drawn there yet, but the number is asserted so that a later change to the ratio has to
        // be a decision rather than a drift. 60% is the requirement's own figure.
        float lowerSlot = inner - switchSize.Y;
        CheckClose(lowerSlot, inner * 0.60f,
                   "the lower band takes the other 60% of the column's inner height");
        Check(lowerSlot > switchSize.Y,
              "the lower band is taller than the switch — the switch is wide, not tall");

        // ---- a controller's taller card splits the same way ----
        var controller = new Node(22, "EventControllerLarge", "controller", Vector3I.Zero,
                                  SensorToolbar(), true);
        BlockPortCatalog.FillEvents(controller);
        controller.CanBeEnabled = true;
        controller.Position = new Vector2(0.30f, 0.50f);

        Vector2 controllerCardTopLeft, controllerCardSize;
        PortGeometry.CardRect(controller, out controllerCardTopLeft, out controllerCardSize);
        Check(controllerCardSize.Y > cardSize.Y, "a controller's card is taller than a standard one");

        Vector2 controllerSwitchTopLeft, controllerSwitchSize;
        Check(PortGeometry.TryPowerSwitchRect(controller, aspect, out controllerSwitchTopLeft,
                                              out controllerSwitchSize),
              "a controller carries a switch as well");

        float controllerInner = controllerCardSize.Y - 2f * VgsConstants.PowerSwitchPadding;
        CheckClose(controllerSwitchSize.Y, controllerInner * 0.40f,
                   "the switch splits a taller card's inner box by the same 40%, not by the "
                   + "standard height");
        CheckClose(controllerSwitchTopLeft.Y - controllerCardTopLeft.Y,
                   VgsConstants.PowerSwitchPadding,
                   "the padding is the same absolute gap on a taller card, not a share of it");
        CheckClose((controllerSwitchSize.X / aspect) / controllerSwitchSize.Y, inkAspect,
                   "the switch keeps its proportions on a taller card");

        // ---- the name starts to the right of the column ----
        float spanLeft, spanRight;
        Check(PortGeometry.TryCardTextSpan(block, aspect, out spanLeft, out spanRight),
              "the card's text span is defined with a switch present");
        Check(spanLeft > switchTopLeft.X + switchSize.X,
              "the block's name starts to the right of the switch, not under it");
        CheckClose(spanLeft, switchTopLeft.X + switchSize.X + VgsConstants.CardButtonTextGap * aspect,
                   "the name clears the column by exactly the row's own text gap");

        // A card without a switch keeps its old span, so the column is paid for only where it is
        // used. This is the assertion that keeps the feature from silently narrowing every card.
        Node plain = MakeTarget(23, "LargeBlockConveyor", "OnOff");
        plain.Position = block.Position;
        float plainLeft, plainRight;
        Check(PortGeometry.TryCardTextSpan(plain, aspect, out plainLeft, out plainRight),
              "a card without a switch still has a text span");
        CheckClose(plainLeft, plain.Position.X + VgsConstants.NodeWidth * PortGeometry.CardTextPaddingRatio,
                   "a card without a switch keeps the plain inner padding");
        Check(spanLeft > plainLeft, "the switch pushes the name to the right, as the requirement asks");

        // ---- the budget: the switch costs the name, and the cost is bounded ----
        //
        // The widest row a card can carry is the controller's four buttons, and the switch sits on
        // the other edge. At the three shapes the editor is used in the name still gets well over
        // the 30% floor; the aspect at which it would fall to the floor drops from about 0.95
        // (ADR-0021) to about 0.83, which is recorded in ADR-0035 as the cost of the column.
        float[] shapes = { 0.5625f, 0.625f, 0.75f };   // 16:9, 16:10, 4:3
        for (int i = 0; i < shapes.Length; i++)
        {
            float a = shapes[i];
            float l, r;
            Check(PortGeometry.TryCardTextSpan(controller, a, out l, out r),
                  "the four-button card with a switch still has a name at aspect " + a);
            Check(r - l >= VgsConstants.NodeWidth * 0.3f,
                  "the switch and the four-button row leave the name 30% of the card at aspect " + a);
        }

        {
            // The column's width is measured on the *controller's* card, not on a standard one:
            // the switch is a fraction of the card it sits on, and the controller's card is one
            // status line taller (ADR-0017). Using NodeHeight here understates the column by 6%
            // and makes this very assertion too lenient — which is what the first version of it
            // did, and why the number below is 0.779 rather than 0.83.
            float rowWidth = 4 * VgsConstants.CardButtonDiameter + 3 * VgsConstants.CardButtonGap;
            float switchWidth = PortGeometry.PowerSwitchInkWidth(controller);
            float column = VgsConstants.CardButtonMargin + switchWidth + VgsConstants.CardButtonTextGap;
            float row = VgsConstants.CardButtonMargin + rowWidth + VgsConstants.CardButtonTextGap;

            // Aspect at which the nameplate falls to the 30% floor: the name fraction is
            // (NodeWidth - (column + row) * aspect) / NodeWidth, so the floor is reached at
            // 0.7 * NodeWidth / (column + row).
            float keepsName = (VgsConstants.NodeWidth - VgsConstants.NodeWidth * 0.3f) / (column + row);

            // 0.779 is the value the geometry produces; it is asserted as a range around it rather
            // than as the same expression again, so that a change to the column or the row shows
            // up here instead of cancelling out.
            Check(keepsName > 0.76f && keepsName < 0.80f,
                  "a four-button card with a switch keeps its name up to aspect ~0.78 (is " + keepsName + ")");

            // And the switch is what costs it: without the column the same card would hold the
            // floor in a square window, which is the number ADR-0021 recorded.
            float withoutColumn = (VgsConstants.NodeWidth - VgsConstants.NodeWidth * 0.3f)
                                  / (row + VgsConstants.NodeWidth * PortGeometry.CardTextPaddingRatio);
            Check(Math.Abs(withoutColumn - 1f) < 0.001f,
                  "without the state column the four-button card keeps its name up to a square "
                  + "window — the figure ADR-0021 recorded, reproduced from the same constants "
                  + "(got " + withoutColumn + ")");

            // And the switch cannot be the thing that pushes the row out of the card: the row is
            // right-anchored and knows nothing about the column.
            Vector2 leftTopLeft, leftSize;
            Check(PortGeometry.TryCardButtonRect(controller, CardButtonKind.Jump, 1.6f,
                                                 out leftTopLeft, out leftSize),
                  "jump button rectangle on a switch-bearing card");
            Check(leftTopLeft.X >= controller.Position.X - 1e-5f,
                  "the row still stays inside a switch-bearing card at aspect 1.6");
        }

        // ---- clicking ----
        //
        // The hit target is the ink plus a small padding, clipped to the card: a control may be
        // forgiving inside its own shape, never outside it, or a click just above the card would
        // switch a block the player did not point at.
        Vector2 centre = switchTopLeft + switchSize * 0.5f;
        Check(PortGeometry.HitPowerSwitch(block, centre, aspect), "the middle of the switch hits it");
        Check(PortGeometry.HitPowerSwitch(block, switchTopLeft, aspect),
              "the switch's own top-left corner hits it");
        Check(PortGeometry.HitPowerSwitch(block,
                                          new Vector2(switchTopLeft.X + switchSize.X,
                                                      switchTopLeft.Y + switchSize.Y), aspect),
              "the switch's own bottom-right corner hits it");

        // Just past the ink, still inside the card: the padding covers it.
        Check(PortGeometry.HitPowerSwitch(block,
                                          new Vector2(centre.X, switchTopLeft.Y + switchSize.Y
                                                                + VgsConstants.PowerSwitchHitPadding * 0.5f),
                                          aspect),
              "the padding below the ink still hits the switch");

        // Well below the switch is the lower slot, which holds no control: a click there must fall
        // through to the card and select the block.
        Check(!PortGeometry.HitPowerSwitch(block,
                                           new Vector2(centre.X, cardTopLeft.Y + cardSize.Y * 0.75f),
                                           aspect),
              "the reserved lower slot is not a hit target");

        // Far to the right is the name, not the switch.
        Check(!PortGeometry.HitPowerSwitch(block,
                                           new Vector2(cardTopLeft.X + cardSize.X * 0.8f, centre.Y),
                                           aspect),
              "the right end of the card is not the switch");

        // Outside the card entirely, even by the padding: refused. This is the vertical clamp, and
        // it is the only one of the four that can fail today — the padding equals the switch's own
        // margin from the border, so the padded box can never reach past the card's left edge. The
        // relationship is asserted right below rather than left to be rediscovered.
        Check(!PortGeometry.HitPowerSwitch(block,
                                           new Vector2(centre.X, cardTopLeft.Y
                                                                - VgsConstants.PowerSwitchHitPadding * 0.5f),
                                           aspect),
              "the padding is clipped to the card, so just above it is a miss");

        Check(VgsConstants.PowerSwitchHitPadding >= VgsConstants.CardButtonMargin,
              "the hit padding is at least the switch's margin from the border — which is why the "
              + "horizontal clamps can never be exercised, and why they are defensive rather than "
              + "load-bearing");

        // The horizontal clamps still have to hold the rule for *any* aspect, because the aspect
        // multiplies the padding and the margin alike: a probe just outside the card's left edge is
        // a miss at every shape the editor is used in.
        float[] clampShapes = { 0.5625f, 1f, 1.6f };
        for (int i = 0; i < clampShapes.Length; i++)
        {
            Check(!PortGeometry.HitPowerSwitch(block,
                                               new Vector2(cardTopLeft.X - 1e-4f, centre.Y),
                                               clampShapes[i]),
                  "just left of the card is a miss at aspect " + clampShapes[i]);
        }

        Check(!PortGeometry.HitPowerSwitch(noSwitch, centre, aspect),
              "a block without a switch is never a hit");

        // A portrait window makes every horizontal length larger, and the clip is what keeps that
        // honest.
        Check(!PortGeometry.HitPowerSwitch(block,
                                           new Vector2(cardTopLeft.X - 0.02f, centre.Y), 1.6f),
              "a wide horizontal padding cannot reach left of the card");

        // ---- the state: three readings, and the functional flag wins ----
        var state = new BlockState();
        Check(state.Power == BlockPower.Unknown, "a fresh state knows nothing about the switch");
        Check(!state.CanToggle, "an unread switch is not a control yet");

        state.SetPower(true, true);
        Check(state.Power == BlockPower.On, "enabled and functional reads as on");
        Check(state.CanToggle, "a switch that is on can be clicked");

        state.SetPower(false, true);
        Check(state.Power == BlockPower.Off, "disabled but functional reads as off");
        Check(state.CanToggle, "a switch that is off can be clicked");

        // The case the game actually produces: a block below its critical integrity still reports
        // Enabled = true and simply stops. Taking the flag at face value would paint it green.
        state.SetPower(true, false);
        Check(state.Power == BlockPower.Unavailable,
              "a damaged block reads as unavailable even while its flag says enabled");
        Check(!state.CanToggle, "a damaged block's switch is not a control");

        state.SetPower(false, false);
        Check(state.Power == BlockPower.Unavailable,
              "a damaged block that is switched off is still unavailable, not merely off");

        // ---- the write side touches the state and nothing else ----
        var graph = new GridGraph(9);
        Node switched = MakeTarget(30, "LargeBlockConveyor", "OnOff");
        switched.CanBeEnabled = true;
        graph.AddNode(switched);

        Node bare = MakeTarget(31, "LargeBlockConveyor", "OnOff");
        graph.AddNode(bare);

        Check(graph.ApplySwitch(30, true, true), "a switch reading for a switchable node is applied");
        Check(switched.State.Power == BlockPower.On, "the reading lands in the node's state");

        Check(!graph.ApplySwitch(31, true, true),
              "a switch reading for a node without a switch is refused");
        Check(bare.State.Power == BlockPower.Unknown,
              "the refused reading leaves the node without a switch untouched");

        Check(!graph.ApplySwitch(4242, true, true), "a reading for a node that is gone is refused");
        Check(graph.NodeCount == 2, "a refused reading does not create a node");

        // The switch must not be able to move anything else on the card: it is a state reading, and
        // the pass that writes it is the cheap one.
        Port firstAction = switched.Actions.Count > 0 ? switched.Actions[0] : null;
        int actionCount = switched.Actions.Count;
        float cardHeight = PortGeometry.CardHeight(switched);
        int buttonCount = PortGeometry.CardButtonCount(switched);

        Check(graph.ApplySwitch(30, false, true), "the same node can be switched off");
        Check(switched.State.Power == BlockPower.Off, "the second reading lands as well");

        Check(switched.Actions.Count == actionCount, "a switch reading does not add or drop ports");
        if (firstAction != null)
        {
            Check(ReferenceEquals(switched.Actions[0], firstAction),
                  "a switch reading does not replace the port objects");
        }
        CheckClose(PortGeometry.CardHeight(switched), cardHeight,
                   "a switch reading does not resize the card");
        Check(PortGeometry.CardButtonCount(switched) == buttonCount,
              "a switch reading does not change the button row");

        // The column is a property of the block's type, not of its power: it must be there while
        // the switch is unread, on, off and unavailable alike. Otherwise the block's name would
        // jump sideways every time somebody flipped the switch.
        Vector2 onTopLeft, onSize, offTopLeft, offSize;
        Check(PortGeometry.TryPowerSwitchRect(switched, aspect, out onTopLeft, out onSize),
              "the column exists while the switch reads off");
        switched.State.Power = BlockPower.Unknown;
        Check(PortGeometry.TryPowerSwitchRect(switched, aspect, out offTopLeft, out offSize),
              "the column exists before the first reading as well");
        CheckClose(onSize.X, offSize.X, "the column's width does not depend on the reading");
        CheckClose(onTopLeft.X, offTopLeft.X, "the column's position does not depend on the reading");
    }

    private static void BlockStateSnapshot()
    {
        // ---- a node always has a state ----
        //
        // The draw path reads `node.State` for every visible card on every frame; it must never
        // have to ask whether one exists. A group has one too, even though it never reads it.
        Node block = MakeNode(1, "LargeBlockConveyor", "OnOff");
        Check(block.State != null, "a block node is created with a state");
        CheckClose(block.State.Integrity, 1f, "a new node's integrity is 1 — not known to be damaged");
        Check(block.State.MetricCount == 0, "a node without a category publishes no metrics");

        var group = new Node("group A", "group A");
        Check(group.State != null, "a group node carries a state as well");
        CheckClose(group.State.Integrity, 1f, "a group's state is inert and reads as undamaged");

        // ---- clamping ----
        //
        // The game can report a negative BuildIntegrity on a block mid-deconstruction and a value
        // above the maximum on one a mod over-welded. Everything downstream treats the number as a
        // fraction, so the clamp belongs at the write, not at each reader.
        var state = new BlockState();
        state.SetIntegrity(0.5f);
        CheckClose(state.Integrity, 0.5f, "a reading inside the range is stored as is");
        Check(state.IsDamaged, "half integrity counts as damaged");

        state.SetIntegrity(-2f);
        CheckClose(state.Integrity, 0f, "a negative reading is clamped to 0");
        state.SetIntegrity(7f);
        CheckClose(state.Integrity, 1f, "a reading above the maximum is clamped to 1");
        Check(!state.IsDamaged, "full integrity is not damaged");

        // The tolerance: the game's own welding leaves the last fraction of a percent unwelded, and
        // a pristine block that reports damage is an alarm the player cannot act on.
        state.SetIntegrity(1f - BlockState.IntegrityTolerance * 0.5f);
        Check(!state.IsDamaged, "a block welded to within the tolerance counts as undamaged");
        state.SetIntegrity(1f - BlockState.IntegrityTolerance * 2f);
        Check(state.IsDamaged, "damage beyond the tolerance is reported");

        // ---- metrics are an indexed, pre-sized array ----
        //
        // The index → meaning mapping is the category descriptor's business (Step 3); what is
        // asserted here is that the container cannot throw from the frame path: an index the
        // category does not publish is ignored rather than an exception in the game loop.
        var withMetrics = new BlockState(2);
        Check(withMetrics.MetricCount == 2, "a state sized for a category carries that many metrics");
        CheckClose(withMetrics.GetMetric(1), 0f, "a metric starts at zero");
        withMetrics.SetMetric(1, 0.25f);
        CheckClose(withMetrics.GetMetric(1), 0.25f, "a metric is written and read back by index");
        CheckClose(withMetrics.GetMetric(9), 0f, "reading an index the category lacks is 0, not a throw");
        withMetrics.SetMetric(-1, 5f);
        withMetrics.SetMetric(9, 5f);
        CheckClose(withMetrics.GetMetric(9), 0f, "writing an index the category lacks is ignored");

        // The damage band: where it is and how wide, in graph units, for the whole range of
        // integrity. It is the only visual the state snapshot produces today, and both of its
        // rules are arithmetic that a game launch would only show as "looks wrong" (ADR-0028).
        Console.WriteLine("=== damage band on the card (ADR-0028) ===");
        DamageBand();

        // ---- the state write touches nothing but the state ----
        //
        // This is the invariant the whole split rests on: the cheap pass must not rebuild the
        // picture. Ports are compared by reference, because that is what the draw path holds —
        // a pass that replaced the Port objects would leave every bar drawn from a stale slot.
        var graph = new GridGraph(7);
        graph.AddNode(block);
        var target = MakeTarget(2, "LargeBlockConveyor", "OnOff");
        graph.AddNode(target);

        graph.TryConnect(1, block.PrimaryEvent.Key, 2, target.Actions[0].Key, new ToolbarCell(0, 0));
        graph.LayoutPorts();

        Check(block.Actions.Count > 0 && block.Events.Count > 0, "the node has ports to protect");
        Check(graph.EdgeCount == 1, "the connection exists before the state pass");

        Port firstAction = block.Actions[0];
        Port firstEvent = block.Events[0];
        int actionCount = block.Actions.Count;
        int eventCount = block.Events.Count;
        int nodeCount = graph.NodeCount;
        int groupCount = graph.GroupCount;
        int edgeCount = graph.EdgeCount;
        int firstActionSlot = firstAction.Slot;
        Vector2 frameBefore = group.FrameSize;

        Check(graph.ApplyIntegrity(1, 0.4f), "an integrity reading for a node in the graph is applied");
        CheckClose(block.State.Integrity, 0.4f, "the reading lands in the node's state");

        Check(ReferenceEquals(block.Actions[0], firstAction),
              "the state pass does not replace the action ports");
        Check(ReferenceEquals(block.Events[0], firstEvent),
              "the state pass does not replace the event ports");
        Check(block.Actions.Count == actionCount && block.Events.Count == eventCount,
              "the state pass does not add or drop ports");
        Check(firstAction.Slot == firstActionSlot, "the state pass does not move a port's slot");
        Check(graph.NodeCount == nodeCount && graph.EdgeCount == edgeCount,
              "the state pass does not add or drop nodes or edges");
        Check(graph.GroupCount == groupCount, "the state pass does not add or drop groups");
        Check(group.FrameSize == frameBefore, "the state pass does not resize a group frame");

        // A block destroyed between the structural pass and the state pass is a normal outcome:
        // the answer is "no node", not an exception and not a new node.
        Check(!graph.ApplyIntegrity(4242, 0.5f), "a reading for a node that is gone is refused");
        Check(graph.NodeCount == nodeCount, "a refused reading does not create a node");

        // The previous value survives a refused reading: one failed read is not evidence of a
        // repair, and a card that flickers between damaged and pristine is worse than a lagging one.
        CheckClose(block.State.Integrity, 0.4f, "a refused reading leaves the previous value alone");
    }

    // ------------------------------------------------------------------ damage band (ADR-0028)
    //
    // The band is "how much of the card is red", and it is the one thing a damaged block shows.
    // Three rules can be wrong in ways that are only visible in the game, and each is arithmetic:
    // its width is the *damage* (not the health), it is anchored to the right edge, and it covers
    // the card and nothing else. An off-by-inversion here would paint every healthy block red and
    // every broken one clean, which is exactly the kind of mistake that survives a code read.
    private static void DamageBand()
    {
        Node block = MakeTarget(10, "LargeBlockConveyor", "OnOff");
        block.Position = new Vector2(0.40f, 0.20f);

        Vector2 bandTopLeft, bandSize;

        // A pristine block draws nothing at all: the card looks exactly as it did before the
        // indicator existed, which is the requirement.
        Check(!PortGeometry.TryDamageRect(block, out bandTopLeft, out bandSize),
              "an undamaged card has no damage band");

        // Width is the damage, from the right edge inward. 70% integrity → 30% of the card red.
        block.State.SetIntegrity(0.7f);
        Check(PortGeometry.TryDamageRect(block, out bandTopLeft, out bandSize),
              "a damaged card has a damage band");
        CheckClose(bandSize.X, VgsConstants.NodeWidth * 0.3f,
                   "a band is as wide as the damage, not as the health (70% integrity → 30% red)");
        CheckClose(bandTopLeft.X + bandSize.X, block.Position.X + VgsConstants.NodeWidth,
                   "the band ends at the card's right edge");
        CheckClose(bandTopLeft.X, block.Position.X + VgsConstants.NodeWidth * 0.7f,
                   "the band starts where the intact part ends");

        // Half integrity → exactly the right half, the case the requirement names explicitly.
        block.State.SetIntegrity(0.5f);
        Check(PortGeometry.TryDamageRect(block, out bandTopLeft, out bandSize),
              "half integrity has a damage band");
        CheckClose(bandSize.X, VgsConstants.NodeWidth * 0.5f, "half integrity paints the right half");

        // Full card height: the band answers a question about the block, and the card is the
        // block. Port bars under the card are what it *does* and are not painted.
        Vector2 cardTopLeft, cardSize;
        PortGeometry.CardRect(block, out cardTopLeft, out cardSize);
        CheckClose(bandTopLeft.Y, cardTopLeft.Y, "the band starts at the card's top edge");
        CheckClose(bandSize.Y, cardSize.Y, "the band covers the card's full height");
        CheckClose(bandSize.Y, VgsConstants.NodeHeight,
                   "a two-line card's band is exactly the card, not the port stack");

        // A destroyed block is a full red card, not an empty one: the clamp at the bottom of the
        // range is where an "invert the fraction" bug would show up as a clean card.
        block.State.SetIntegrity(0f);
        Check(PortGeometry.TryDamageRect(block, out bandTopLeft, out bandSize),
              "a destroyed block still has a band");
        CheckClose(bandSize.X, VgsConstants.NodeWidth, "zero integrity paints the whole card");

        // The tolerance belongs to the state, and the geometry must use the same one: a block
        // welded to within it draws nothing, exactly as BlockState.IsDamaged says.
        block.State.SetIntegrity(1f - BlockState.IntegrityTolerance * 0.5f);
        Check(!PortGeometry.TryDamageRect(block, out bandTopLeft, out bandSize),
              "the geometry uses the state's own damage tolerance");

        // A controller's card is a line taller (ADR-0017) and the band follows the card, not the
        // standard height — otherwise the band would stop a line short on exactly the cards that
        // are the tallest.
        Node controller = MakeNode(11, "EventControllerBlock", TimerToolbar(), "OnOff");
        controller.State.SetIntegrity(0.4f);
        Vector2 controllerCardTopLeft, controllerCardSize;
        PortGeometry.CardRect(controller, out controllerCardTopLeft, out controllerCardSize);
        Check(PortGeometry.TryDamageRect(controller, out bandTopLeft, out bandSize),
              "a controller's card can show damage too");
        CheckClose(bandSize.Y, controllerCardSize.Y,
                   "the band follows a taller card instead of the standard height");

        // A group has no block, so it has no integrity and never paints a band — even though it
        // carries a state object like every other node.
        var group = new Node("group D", "group D");
        group.State.SetIntegrity(0.2f);
        Check(!PortGeometry.TryDamageRect(group, out bandTopLeft, out bandSize),
              "a group never shows a damage band");

        // ---- the band stays inside its card (the defect found in game) ----
        //
        // The first version widened a sub-pixel band by comparing a *normalized* width against
        // the number 1 — the whole screen — so the comparison was always true and every damaged
        // card painted a band a full screen wide. These assertions pin the rule down in graph
        // units, where the mistake cannot hide behind a unit confusion.
        Node clamped = MakeTarget(12, "LargeBlockConveyor", "OnOff");
        clamped.Position = new Vector2(0.50f, 0.30f);
        clamped.State.SetIntegrity(0.99f);
        Check(PortGeometry.TryDamageRect(clamped, out bandTopLeft, out bandSize),
              "a barely damaged card has a band");

        Vector2 cardLeft, cardSize2;
        PortGeometry.CardRect(clamped, out cardLeft, out cardSize2);

        // No floor: the band is what the damage says, however thin.
        Vector2 noFloorTopLeft = bandTopLeft, noFloorSize = bandSize;
        PortGeometry.FitDamageBand(ref noFloorTopLeft, ref noFloorSize, clamped.Position,
                                   cardSize2, 0f);
        CheckClose(noFloorSize.X, VgsConstants.NodeWidth * 0.01f,
                   "without a floor a 1% scratch is 1% of the card wide");
        CheckClose(noFloorTopLeft.X + noFloorSize.X, cardLeft.X + cardSize2.X,
                   "the band still ends at the card's right edge");

        // With a floor wider than the damage, the band grows to the floor and stays flush right.
        float floor = VgsConstants.NodeWidth * 0.02f;
        Vector2 flooredTopLeft = bandTopLeft, flooredSize = bandSize;
        PortGeometry.FitDamageBand(ref flooredTopLeft, ref flooredSize, clamped.Position,
                                   cardSize2, floor);
        CheckClose(flooredSize.X, floor, "a band thinner than the floor grows to the floor");
        CheckClose(flooredTopLeft.X + flooredSize.X, cardLeft.X + cardSize2.X,
                   "the floored band is still flush with the card's right edge");
        CheckClose(flooredSize.Y, cardSize2.Y, "the band keeps the card's height");
        Check(flooredTopLeft.X >= cardLeft.X, "the floored band never starts left of the card");

        // A floor wider than the card cannot push the band out of it: the card wins.
        Vector2 hugeTopLeft = bandTopLeft, hugeSize = bandSize;
        PortGeometry.FitDamageBand(ref hugeTopLeft, ref hugeSize, clamped.Position,
                                   cardSize2, VgsConstants.NodeWidth * 3f);
        CheckClose(hugeSize.X, cardSize2.X, "a floor wider than the card is clamped to the card");
        CheckClose(hugeTopLeft.X, cardLeft.X, "the clamped band starts at the card's left edge");
        CheckClose(hugeTopLeft.X + hugeSize.X, cardLeft.X + cardSize2.X,
                   "the clamped band ends at the card's right edge");

        // A fully damaged card is exactly the card — the case that must not look like a spill.
        clamped.State.SetIntegrity(0f);
        Check(PortGeometry.TryDamageRect(clamped, out bandTopLeft, out bandSize),
              "a destroyed card has a band");
        PortGeometry.FitDamageBand(ref bandTopLeft, ref bandSize, clamped.Position, cardSize2, floor);
        CheckClose(bandSize.X, cardSize2.X, "a destroyed card's band is exactly the card's width");
        CheckClose(bandTopLeft.X, cardLeft.X, "a destroyed card's band starts at the card's left");
        Check(bandTopLeft.X + bandSize.X <= cardLeft.X + cardSize2.X + 1e-6f,
              "no band ever extends past the card's right edge");
    }

    // ------------------------------------------------------------------ text field (ADR-0024)
    //
    // Caret and selection are the part of the search field that can be wrong in ways nobody sees
    // until it corrupts what the player typed: an insertion at the wrong index, a selection whose
    // ends crossed, a backspace that eats the character after the caret. All of it is pure data,
    // so all of it is asserted here.
    private static void TextFieldEditing()
    {
        // ---- insertion and the caret ----
        var editor = new TextEditor(10);
        Check(editor.Text == string.Empty && editor.Caret == 0 && !editor.HasSelection,
              "a fresh editor is empty with the caret at the start");

        editor.Insert('a');
        editor.Insert('b');
        editor.Insert('c');
        Check(editor.Text == "abc", "characters are inserted in order");
        Check(editor.Caret == 3, "the caret follows the insertion");

        // Inserting in the middle is the case a naive "append" gets wrong.
        editor.MoveTo(1, false);
        editor.Insert('X');
        Check(editor.Text == "aXbc", "a character is inserted at the caret, not at the end");
        Check(editor.Caret == 2, "the caret moves past what was inserted");

        // ---- the length limit ----
        var limited = new TextEditor(3);
        limited.Insert("abcdef");
        Check(limited.Text == "abc", "inserting past the limit is cut to the room left");
        Check(limited.Caret == 3, "the caret stops at the limit");
        Check(!limited.Insert('z'), "an insert with no room reports that nothing changed");

        // A selection frees room even at the limit: replacing three characters with three works.
        limited.SelectAll();
        Check(limited.Insert("xy"), "replacing a selection at the limit is allowed");
        Check(limited.Text == "xy", "the selection is replaced, not appended");

        // ---- backspace and delete ----
        var edit2 = new TextEditor(10);
        edit2.Insert("abcd");

        edit2.MoveTo(2, false);
        Check(edit2.DeleteBackward(), "backspace removes a character");
        Check(edit2.Text == "acd", "backspace removes the character BEFORE the caret");
        Check(edit2.Caret == 1, "the caret moves back with the deletion");

        Check(edit2.DeleteForward(), "delete removes a character");
        Check(edit2.Text == "ad", "delete removes the character AFTER the caret");
        Check(edit2.Caret == 1, "the caret stays put on a forward delete");

        edit2.MoveTo(0, false);
        Check(!edit2.DeleteBackward(), "backspace at the start does nothing");
        edit2.MoveTo(edit2.Length, false);
        Check(!edit2.DeleteForward(), "delete at the end does nothing");

        // ---- selection: anchor and caret, both directions ----
        var sel = new TextEditor(10);
        sel.Insert("hello");

        sel.MoveTo(1, false);
        sel.MoveTo(4, true);
        Check(sel.HasSelection, "moving with extend selects");
        Check(sel.SelectionStart == 1 && sel.SelectionEnd == 4,
              "a forward selection reports its ends in order");
        Check(sel.SelectedText == "ell", "the selected text is the span between the ends");

        // A backward selection is the case that a "start + length" model gets wrong: the caret
        // ends up BEFORE the anchor, and the span must still be reported low-to-high.
        var back = new TextEditor(10);
        back.Insert("hello");
        back.MoveTo(4, false);
        back.MoveTo(1, true);
        Check(back.Caret == 1 && back.Anchor == 4,
              "the caret is the moving end and the anchor the fixed one");
        Check(back.SelectionStart == 1 && back.SelectionEnd == 4,
              "a backward selection still reports start before end");
        Check(back.SelectedText == "ell",
              "a backward selection selects the same span as the forward one");

        // Without extend, a held selection collapses to the edge the caret moves towards.
        sel.MoveHorizontal(1, false);
        Check(!sel.HasSelection, "an unshifted arrow collapses the selection");
        Check(sel.Caret == 4, "the caret lands on the edge it moved towards");

        // With extend it grows instead: from a collapsed caret at 4, shift+left selects 3..4.
        sel.MoveHorizontal(-1, true);
        Check(sel.HasSelection, "shift+arrow extends the selection");
        Check(sel.Caret == 3 && sel.Anchor == 4, "shift+left moves only the caret");

        // Typing over a selection replaces it — the whole point of selecting.
        sel.SelectAll();
        sel.Insert("bye");
        Check(sel.Text == "bye", "typing over a selection replaces it");
        Check(!sel.HasSelection, "the selection is consumed by the replacement");

        // ---- deleting a selection ----
        var del = new TextEditor(10);
        del.Insert("abcdef");
        del.MoveTo(1, false);
        del.MoveTo(4, true);
        Check(del.DeleteBackward(), "backspace deletes a selection");
        Check(del.Text == "aef", "the whole selection is removed, not one character");
        Check(del.Caret == 1, "the caret sits where the selection started");

        // ---- a rejected edit leaves the text untouched ----
        //
        // The caller uses the false return to decide whether anything changed; if a refused edit
        // had already mutated the text, the field would drift on a keystroke that "did nothing".
        var refused = new TextEditor(3);
        refused.Insert("abc");
        string beforeRefusal = refused.Text;
        int caretBeforeRefusal = refused.Caret;
        Check(!refused.Insert('z'), "an insert at the limit is refused");
        Check(refused.Text == beforeRefusal, "a refused insert leaves the text unchanged");
        Check(refused.Caret == caretBeforeRefusal, "a refused insert leaves the caret unchanged");

        refused.MoveTo(0, false);
        Check(!refused.DeleteBackward(), "a refused delete reports false");
        Check(refused.Text == beforeRefusal, "a refused delete leaves the text unchanged");

        // ---- collapsing a selection going LEFT ----
        //
        // The right-hand case is asserted above; the left one is a different branch of the same
        // rule and the one a naive `caret ± 1` gets wrong.
        var left = new TextEditor(10);
        left.Insert("abcdef");
        left.MoveTo(1, false);
        left.MoveTo(4, true);
        left.MoveHorizontal(-1, false);
        Check(!left.HasSelection, "an unshifted left arrow collapses the selection too");
        Check(left.Caret == 1, "collapsing left lands on the selection's start");

        // ---- a paste longer than the room left, with a selection ----
        //
        // The room is computed from the text *without* the selection, so replacing frees exactly
        // the selection's length. "abcde" minus "bcd" leaves "ae" (2 chars) in a 5-char field, so
        // 3 characters fit and the paste is cut to "XYZ" — the ends must survive intact.
        var tight = new TextEditor(5);
        tight.Insert("abcde");
        tight.MoveTo(1, false);
        tight.MoveTo(4, true);
        tight.Insert("XYZWVU");
        Check(tight.Text == "aXYZe", "a paste is cut to the room the selection freed");
        Check(tight.Caret == 4, "the caret lands right after the pasted text");

        // ---- clamping ----
        var clamp = new TextEditor(10);
        clamp.Insert("ab");
        clamp.MoveTo(-5, false);
        Check(clamp.Caret == 0, "a caret index before the text is clamped to the start");
        clamp.MoveTo(99, false);
        Check(clamp.Caret == 2, "a caret index past the text is clamped to the end");

        // ---- multi-character insert (a paste) ----
        //
        // The limit is deliberately larger than the strings so this checks placement, not
        // truncation: truncation has its own checks above.
        var paste = new TextEditor(32);
        paste.Insert("abc");
        paste.MoveTo(1, false);
        paste.Insert("-pasted-");
        Check(paste.Text == "a-pasted-bc", "a paste inserts at the caret, not at the end");
        Check(paste.Caret == 9, "the caret lands after the pasted text");

        // A paste over a selection replaces it, like typing does.
        paste.SelectAll();
        paste.Insert("z");
        Check(paste.Text == "z", "a paste over a selection replaces it");

        // ---- SelectAll ----
        var all = new TextEditor(10);
        all.Insert("xyz");
        all.SelectAll();
        Check(all.SelectionStart == 0 && all.SelectionEnd == 3,
              "select-all covers the whole text");
        Check(all.SelectedText == "xyz", "select-all selects everything, in order");

        // ---- SetText and Clear ----
        var set = new TextEditor(5);
        set.SetText("abcdefg");
        Check(set.Text == "abcde", "SetText respects the limit");
        Check(set.Caret == 5, "SetText parks the caret at the end");
        set.Clear();
        Check(set.Text == string.Empty && set.Caret == 0, "Clear empties the field");
        set.SetText(null);
        Check(set.Text == string.Empty, "SetText(null) is an empty field, not a crash");
    }

    /// <summary>
    /// A measurer that gives every character the same width, so the arithmetic below can be
    /// stated in characters rather than in pixels of a font the gate does not have.
    /// </summary>
    private static SearchField FieldWithFixedCharWidth(float perChar)
    {
        return new SearchField(text => text.Length * perChar);
    }

    /// <summary>
    /// The search field's layout (Step 4.2): where the caret sits, how far the text scrolls, and
    /// which slice of the query is painted.
    ///
    /// This is the arithmetic that breaks silently — a caret that stops one character short, a
    /// scroll that lets the caret leave the box, a slice that starts one character late — and it
    /// is invisible in the game until someone types a long query. Extracting the field from the
    /// screen is what makes it checkable at all: its one game dependency (font measurement) is an
    /// injected delegate.
    /// </summary>
    private static void SearchFieldLayout()
    {
        const int frame = 100;

        // ---- an empty field ----
        var empty = FieldWithFixedCharWidth(10f);
        Check(!empty.HasText, "an empty field has no text");
        Check(!empty.HasSelection, "an empty field has no selection");
        empty.EnsureLayout(100f);
        CheckClose(empty.CaretX, 0f, "an empty field puts the caret at the origin");
        CheckClose(empty.ScrollX, 0f, "an empty field does not scroll");

        // ---- short text that fits: no scroll ----
        var fits = FieldWithFixedCharWidth(10f);
        fits.Insert("abc", frame);
        Check(fits.HasText, "typed characters make the field non-empty");
        fits.EnsureLayout(100f);
        CheckClose(fits.CaretX, 30f, "the caret sits after three 10-unit characters");
        CheckClose(fits.ScrollX, 0f, "text that fits is not scrolled");
        Check(fits.DisplayText == "abc", "a field that fits paints the whole query");
        CheckClose(fits.FirstVisibleX, 0f, "the first visible character is the first one");

        // ---- the caret is kept inside the box ----
        //
        // Six characters at 10 units in a 50-unit box: the caret belongs at 60, which is outside,
        // so the text scrolls by exactly the overflow (10) and the caret lands on the right edge.
        var scroll = FieldWithFixedCharWidth(10f);
        scroll.Insert("abcdef", frame);
        scroll.EnsureLayout(50f);
        CheckClose(scroll.CaretX, 60f, "the caret's position is measured from the text origin");
        CheckClose(scroll.ScrollX, 10f, "the text scrolls by exactly the caret's overflow");
        CheckClose(scroll.CaretX - scroll.ScrollX, 50f,
                   "the scrolled caret sits exactly on the right edge of the box");

        // The painted slice starts at the first character whose left edge is still visible, and
        // the slice's origin is that character's own x — the two must agree, or the text jumps.
        Check(scroll.DisplayText == "bcdef", "the painted slice drops the characters scrolled off");
        CheckClose(scroll.FirstVisibleX, 10f, "the painted slice's origin is the first visible char");

        // ---- scrolling back ----
        //
        // Moving the caret home must undo the scroll: a field that stayed scrolled with the caret
        // at index 0 would paint the query's tail while the caret sat on its first character.
        scroll.MoveTo(0, false, frame);
        scroll.EnsureLayout(50f);
        CheckClose(scroll.CaretX, 0f, "home puts the caret at the origin");
        CheckClose(scroll.ScrollX, 0f, "moving home scrolls the text back");
        Check(scroll.DisplayText == "abcdef", "an unscrolled field paints the whole query again");

        // ---- the layout is cached, and invalidated by every edit ----
        //
        // The cache is what keeps the font metrics out of the draw path (R010), so a stale cache
        // would paint the caret at the previous position; a cache that is never reused would
        // measure the font sixty times a second for a field nobody is typing into.
        var cached = FieldWithFixedCharWidth(10f);
        cached.Insert("abc", frame);
        cached.EnsureLayout(100f);
        float firstMeasure = cached.CaretX;
        cached.EnsureLayout(100f);
        CheckClose(cached.CaretX, firstMeasure, "an unchanged layout is reused, not recomputed");

        cached.Insert('d', frame);
        cached.EnsureLayout(100f);
        CheckClose(cached.CaretX, 40f, "an edit invalidates the cached layout");

        // A width change must also invalidate it: the "x" appearing shrinks the text area, and a
        // caret placed for the wider box would sit past the right edge.
        var rewiden = FieldWithFixedCharWidth(10f);
        rewiden.Insert("abcdef", frame);
        rewiden.EnsureLayout(100f);
        CheckClose(rewiden.ScrollX, 0f, "a wide box needs no scroll for six characters");
        rewiden.EnsureLayout(50f);
        CheckClose(rewiden.ScrollX, 10f, "narrowing the box scrolls the text");

        // ---- the "changed" flag ----
        //
        // The screen rebuilds its filter from this flag, so it must be set by every edit and
        // cleared exactly once — a flag that stayed set would re-split the query every frame, and
        // one that was never set would leave the picture filtering by the previous query.
        var changed = FieldWithFixedCharWidth(10f);
        Check(!changed.TakeChanged(), "a fresh field is not marked changed");
        changed.Insert('a', frame);
        Check(changed.TakeChanged(), "typing marks the field changed");
        Check(!changed.TakeChanged(), "the mark is consumed by the first read");

        changed.MoveTo(0, false, frame);
        Check(!changed.TakeChanged(), "moving the caret is not a text change");

        Check(!changed.Insert('\u0001', frame), "a control character is dropped");
        Check(!changed.TakeChanged(), "a dropped character is not a change");

        changed.Clear(frame);
        Check(changed.TakeChanged(), "clearing marks the field changed");
        Check(!changed.HasText, "clearing empties the query");

        // ---- the caret's blink ----
        //
        // The phase restarts on every edit: a caret that kept blinking through a keystroke would
        // be invisible exactly when the player looks for it.
        var blink = FieldWithFixedCharWidth(10f);
        blink.Insert('a', 100);
        Check(blink.CaretVisible(100), "the caret is lit right after an edit");
        Check(blink.CaretVisible(100 + VgsConstants.CaretBlinkFrames - 1),
              "the caret is still lit at the end of the first half-cycle");
        Check(!blink.CaretVisible(100 + VgsConstants.CaretBlinkFrames),
              "the caret is dark on the second half-cycle");
        Check(blink.CaretVisible(100 + VgsConstants.CaretBlinkFrames * 2),
              "the caret comes back on the next cycle");

        blink.MoveTo(0, false, 500);
        Check(blink.CaretVisible(500), "moving the caret restarts the blink");
    }

    /// <summary>
    /// The view state (Step 4.3): what counts as "the view changed", and when a write is due.
    ///
    /// The subtle rule is that the throttle advances on the <em>attempt</em>, not on the success
    /// (ADR-0022): a grid that refuses the write must not make the screen retry on every frame.
    /// That is the failure this section exists to catch, because it is invisible in the game
    /// until a grid is being torn down while the editor is open.
    /// </summary>
    private static void ViewStateWrites()
    {
        var view = new ViewState(new Vector2(0.1f, 0.2f), 1.5f, false);

        // A view that was just read from the grid is already what the grid holds, so it is
        // recorded as saved: writing it back would be a no-op that costs a format and a
        // dictionary write. The screen used to leave the saved values "unwritten" and wrote the
        // same camera back two seconds after opening.
        Check(!view.HasChanged, "a freshly loaded view matches what the grid already holds");
        Check(!view.TakeChanged(0), "a freshly loaded view is not due for a write");

        // ---- each of the three values counts as a change ----
        view.Camera = new Vector2(0.3f, 0.2f);
        Check(view.HasChanged, "moving the camera is a change");
        view.RememberSaved(0);

        view.Zoom = 2f;
        Check(view.HasChanged, "changing the zoom is a change");
        view.RememberSaved(0);

        view.ShowHidden = true;
        Check(view.HasChanged, "changing the hidden mode is a change");
        view.RememberSaved(0);
        Check(!view.HasChanged, "recording again clears the change");

        // ---- the throttle ----
        view.Camera = new Vector2(0.4f, 0.2f);
        Check(!view.TakeChanged(VgsConstants.ViewSaveIntervalFrames - 1),
              "a change is not due before the throttle interval has passed");
        Check(view.TakeChanged(VgsConstants.ViewSaveIntervalFrames),
              "a change is due once the throttle interval has passed");

        // ---- the throttle advances on the attempt, not on the success ----
        //
        // This is the whole point. `TakeChanged` returned true above, which means the caller is
        // about to write; if that write fails, the saved values are untouched and `HasChanged`
        // stays true. The next attempt must therefore wait for the interval rather than fire on
        // the very next frame — otherwise a refusing grid costs a string format and a dictionary
        // write every frame for as long as the refusal lasts.
        Check(view.HasChanged, "a failed write leaves the view looking changed");
        Check(!view.TakeChanged(VgsConstants.ViewSaveIntervalFrames + 1),
              "a failed write does not cause a retry on the next frame");
        Check(view.TakeChanged(VgsConstants.ViewSaveIntervalFrames * 2),
              "a failed write is retried at the normal cadence");

        // A successful write after all that still records the view, and the change clears.
        view.RememberSaved(VgsConstants.ViewSaveIntervalFrames * 2);
        Check(!view.HasChanged, "a successful write makes the view the new baseline");
    }

    /// <summary>
    /// The two string tables and the facade over them (ADR-0033).
    ///
    /// The tables are data, so completeness is a gate's job rather than the compiler's. What can
    /// actually go wrong with a key → value table, and what is asserted here:
    ///
    ///  * <b>a key present in one language and not the other</b> — compared as sets, in both
    ///    directions;
    ///  * <b>a value that is empty</b> — compiles, paints a blank control, and reads as a layout
    ///    bug rather than as a missing translation;
    ///  * <b>a broken format string</b> — the call sites pass arguments by position, so a
    ///    translation that drops <c>{1}</c> prints the wrong value and one that mistypes a brace
    ///    throws <c>FormatException</c> inside a draw path. Every value is formatted with as many
    ///    arguments as it asks for, in both languages;
    ///  * <b>a placeholder that means a different thing in each language</b> — compared as sets;
    ///  * <b>a table that is a copy of the other one</b> — compared entry by entry, and the count
    ///    of entries that differ must stay near the whole;
    ///  * <b>a key nobody reads</b> — a string the player will never see translated. Detected by
    ///    counting: the table must not hold more keys than the facade has string properties;
    ///  * <b>a property whose key does not exist</b> — the facade falls back to the key itself,
    ///    which is an all-caps mark no sentence looks like, so asking every property in both
    ///    languages and refusing a key-shaped answer catches a typo without reading source.
    /// </summary>
    private static void Localization()
    {
        IReadOnlyDictionary<string, string> english = VgsStrings.English;
        IReadOnlyDictionary<string, string> russian = VgsStrings.Russian;

        Check(english.Count > 0, "the English table is not empty");
        Check(russian.Count > 0, "the Russian table is not empty");
        Check(SameKeys(english, russian), "both languages have exactly the same keys");

        int differing = 0;

        foreach (KeyValuePair<string, string> entry in english)
        {
            string en = entry.Value;
            string ru;
            if (!russian.TryGetValue(entry.Key, out ru)) continue;   // already reported by SameKeys

            Check(!string.IsNullOrEmpty(en), entry.Key + " is not empty in English");
            Check(!string.IsNullOrEmpty(ru), entry.Key + " is not empty in Russian");
            if (string.IsNullOrEmpty(en) || string.IsNullOrEmpty(ru)) continue;

            if (en != ru) differing++;

            List<int> enSlots = Placeholders(en);
            List<int> ruSlots = Placeholders(ru);
            Check(SameSlots(enSlots, ruSlots),
                  entry.Key + " uses the same placeholders in both languages");

            // Formatting proves the string is well formed, not just that it looks like it. The
            // arguments are placeholders themselves, so a swapped one is visible in the output.
            int arity = 0;
            for (int i = 0; i < enSlots.Count; i++) if (enSlots[i] + 1 > arity) arity = enSlots[i] + 1;
            if (arity == 0) continue;

            var args = new object[arity];
            for (int i = 0; i < arity; i++) args[i] = "<" + i + ">";

            Check(Formats(en, args), entry.Key + " formats in English");
            Check(Formats(ru, args), entry.Key + " formats in Russian");
        }

        Check(differing >= english.Count - 4,
              "the two tables are translations, not copies (" + differing + " of " + english.Count + " differ)");

        // ---- the facade ----

        // The mod starts in the fallback: before a session exists there is no config to ask, and a
        // screen built in that window must still have text.
        Check(VgsText.Language == VgsLanguage.English, "the editor starts in English");

        // Every property, in every language. Reflection rather than a list, because a list stops
        // covering the newest string — the one most likely to be wrong.
        PropertyInfo[] properties = typeof(VgsText).GetProperties(BindingFlags.Public | BindingFlags.Static);
        int strings = 0;

        foreach (VgsLanguage language in new[] { VgsLanguage.English, VgsLanguage.Russian })
        {
            VgsText.UseLanguage(language);

            foreach (PropertyInfo property in properties)
            {
                if (property.PropertyType != typeof(string)) continue;   // `Language` is the enum
                if (language == VgsLanguage.English) strings++;

                string value = (string)property.GetValue(null, null);
                Check(!string.IsNullOrEmpty(value),
                      "VgsText." + property.Name + " is not empty in " + language);
                Check(!LooksLikeKey(value),
                      "VgsText." + property.Name + " resolves in " + language
                      + " (got \"" + value + "\", so its key is missing from the table)");
            }
        }

        // A key no property reads is a string that is never shown; a property count below the key
        // count means exactly that.
        Check(strings == english.Count,
              "every key is read by a property (keys " + english.Count + ", properties " + strings + ")");

        // ---- switching ----

        // The loop above left the facade on the last language it asked. Put it back to the
        // fallback first: otherwise "switching to Russian reports a change" would be asserting
        // the opposite of what it says, and would pass for the wrong reason on a machine where
        // the loop happened to end elsewhere.
        VgsText.UseLanguage(VgsLanguage.English);
        Check(VgsText.Language == VgsLanguage.English, "the facade can be put back to English");

        Check(VgsText.UseLanguage(VgsLanguage.Russian), "switching to Russian reports a change");
        Check(VgsText.Language == VgsLanguage.Russian, "the facade reports Russian after the switch");
        Check(VgsText.CloseButton == russian["CLOSE_BUTTON"], "the facade answers from the Russian table");
        Check(!VgsText.UseLanguage(VgsLanguage.Russian), "switching to the language in use reports no change");

        // A language the mod has no table for is not an error: it falls back to English.
        VgsText.UseLanguage(VgsLanguage.English);
        Check(VgsText.EditorTitle == english["EDITOR_TITLE"], "switching back restores English");

        // Session teardown returns the mod to the fallback, so a language read in one world cannot
        // outlive it. `Reset` is internal, which the offline build can reach because it compiles
        // these checks and the mod's sources into one assembly.
        VgsText.UseLanguage(VgsLanguage.Russian);
        VgsText.Reset();
        Check(VgsText.Language == VgsLanguage.English, "Reset returns the facade to the fallback");

        // ---- what must NOT be translated ----

        // The fallback glyphs are chosen for existing in every font the editor may fall back to.
        // A translated glyph would be a letter from another alphabet, which is the most likely
        // one to be missing — and a missing glyph draws as a box on the card.
        Check(IsAscii(VgsText.ModesGlyph), "the modes glyph is ASCII");
        Check(IsAscii(VgsText.SingleButtonGlyph), "the bind glyph is ASCII");
        Check(IsAscii(VgsText.TrueButtonGlyph), "the 'yes' glyph is ASCII");
        Check(IsAscii(VgsText.FalseButtonGlyph), "the 'no' glyph is ASCII");
        Check(IsAscii(VgsText.PortRemoveGlyph), "the port remove glyph is ASCII");
        Check(IsAscii(VgsText.JumpGlyph), "the jump glyph is ASCII");
        Check(IsAscii(VgsText.WatchGlyph), "the watch glyph is ASCII");
        Check(IsAscii(VgsText.WatchSignLower), "the '<=' sign is ASCII");
        Check(IsAscii(VgsText.WatchSignHigher), "the '>=' sign is ASCII");

        // The threshold is written by the card and read back by the game's own comparison; the
        // format is invariant culture on purpose (a comma decimal separator would be a different
        // number to a player, and a different string to the game).
        Check(0.5f.ToString(VgsText.WatchThresholdFormat, CultureInfo.InvariantCulture) == "0.50",
              "the threshold format is invariant and two-place");
    }

    /// <summary>
    /// True when two tables hold exactly the same keys. Compares both directions, so a key only
    /// the translation has is a failure too — that is a string the default language lost.
    /// </summary>
    private static bool SameKeys(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count) return false;

        foreach (string key in left.Keys) if (!right.ContainsKey(key)) return false;
        foreach (string key in right.Keys) if (!left.ContainsKey(key)) return false;

        return true;
    }

    /// <summary>
    /// True when a value looks like a key rather than a sentence: capitals, digits and underscores.
    ///
    /// This is what makes "the key is missing from the table" detectable without reading source.
    /// The facade falls back to the key, and a key is an all-caps mark — no wording in either
    /// language is written that way. A translation that happened to be, would fail here, and that
    /// is the right outcome: on screen it would be indistinguishable from a bug.
    /// </summary>
    private static bool LooksLikeKey(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;

        bool sawLetter = false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c >= 'A' && c <= 'Z') { sawLetter = true; continue; }
            if (c >= '0' && c <= '9') continue;
            if (c == '_') continue;
            return false;
        }

        return sawLetter && value.IndexOf('_') >= 0;
    }

    /// <summary>
    /// The <c>{n}</c> indices of a format string, in the order they appear.
    ///
    /// Written by hand rather than with a regex: this has to run inside the offline gate, and a
    /// scanner that cannot backtrack is easier to trust than a pattern with three groups.
    /// </summary>
    private static List<int> Placeholders(string text)
    {
        var found = new List<int>();
        if (text == null) return found;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') continue;

            int j = i + 1;
            int value = 0;
            bool digits = false;
            while (j < text.Length && text[j] >= '0' && text[j] <= '9')
            {
                value = value * 10 + (text[j] - '0');
                digits = true;
                j++;
            }

            if (digits && j < text.Length && text[j] == '}')
            {
                found.Add(value);
                i = j;
            }
        }

        return found;
    }

    private static bool SameSlots(List<int> left, List<int> right)
    {
        if (left.Count != right.Count) return false;

        var a = new List<int>(left);
        var b = new List<int>(right);
        a.Sort();
        b.Sort();
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static bool Formats(string format, object[] args)
    {
        try
        {
            string result = string.Format(CultureInfo.InvariantCulture, format, args);
            return result != null;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsAscii(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < text.Length; i++) if (text[i] > 127) return false;
        return true;
    }
}
