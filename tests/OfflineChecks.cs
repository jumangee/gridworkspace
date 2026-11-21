using System;
using System.Collections.Generic;
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

        Console.WriteLine("=== card and port geometry ===");
        Geometry();

        Console.WriteLine("=== bind buttons ===");
        BindButtons();

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

        Console.WriteLine("=== block group action rule (ADR-0015) ===");
        GroupActionRule();

        Console.WriteLine("=== block group nodes (ADR-0015) ===");
        GroupNodes();

        Console.WriteLine("=== connections to a block group (ADR-0015) ===");
        GroupEdges();

        Console.WriteLine("=== block group frame geometry (ADR-0015) ===");
        GroupGeometry();

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

        Check(!graph.TryConnect(1, "Triggered", 1, "TriggerNow", new ToolbarCell(0, 0)),
              "self-loop is rejected");
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
    /// The bind buttons: one "+" for a single-group block, a "T"/"F" pair for a yes/no one.
    /// What matters is that the control exists exactly where it can be clicked, that the two
    /// discs do not overlap, and that each of them reports its own group.
    /// </summary>
    private static void BindButtons()
    {
        const float aspect = 1f;

        Node timer = MakeNode(1, "LargeBlockTimerBlock");
        timer.Position = new Vector2(0.2f, 0.2f);

        Vector2 plusCenter;
        Check(PortGeometry.TryBindButton(timer, PortGroup.Single, aspect, out plusCenter),
              "a single-group block has a bind button");
        CheckClose(plusCenter.Y, timer.Position.Y + VgsConstants.NodeHeight * 0.5f,
                   "the plus is vertically centred in the card");
        Check(!PortGeometry.HasGroupButtons(timer), "a single-group block shows no T/F pair");

        Vector2 noButton;
        Check(!PortGeometry.TryBindButton(timer, PortGroup.Yes, aspect, out noButton),
              "a single-group block has no separate yes button");

        CheckClose(VgsConstants.PlusButtonRadius * 2f, VgsConstants.NodeHeight * 0.75f,
                   "plus button diameter is 75% of the card height");

        PortGroup hit;
        Check(PortGeometry.HitBindButton(timer, plusCenter, aspect, out hit), "the plus centre hits");
        Check(hit == PortGroup.Single, "the plus reports the single group");
        Check(!PortGeometry.HitBindButton(timer, plusCenter + new Vector2(0.05f, 0f), aspect, out hit),
              "far to the right of the plus is a miss");

        // A yes/no block: two discs, one above the other, each hit-testing to its own group.
        Node sensor = MakeNode(2, "LargeBlockSensor", SensorToolbar());
        sensor.Position = new Vector2(0.2f, 0.4f);
        Check(PortGeometry.HasGroupButtons(sensor), "a yes/no block shows the T/F pair");

        Vector2 trueCenter, falseCenter;
        Check(PortGeometry.TryBindButton(sensor, PortGroup.Yes, aspect, out trueCenter), "true button");
        Check(PortGeometry.TryBindButton(sensor, PortGroup.No, aspect, out falseCenter), "false button");
        Check(trueCenter.Y < falseCenter.Y, "the true button is above the false one");
        CheckClose(trueCenter.X, falseCenter.X, "both buttons sit on the same vertical line");

        // The discs must not overlap, or the pair reads as one blob and a click is ambiguous.
        Check(falseCenter.Y - trueCenter.Y > VgsConstants.GroupButtonRadius * 2f,
              "the two group buttons do not overlap");

        // Both stay inside the card.
        float halfWidth = VgsConstants.GroupButtonRadius * aspect;
        Check(trueCenter.X + halfWidth <= sensor.Position.X + VgsConstants.NodeWidth + 1e-5f,
              "the true button stays inside the card's right edge");
        Check(trueCenter.X - halfWidth >= sensor.Position.X,
              "the true button stays inside the card's left edge");
        Check(trueCenter.Y - VgsConstants.GroupButtonRadius >= sensor.Position.Y - 1e-5f,
              "the true button stays inside the card's top edge");
        Check(falseCenter.Y + VgsConstants.GroupButtonRadius
              <= sensor.Position.Y + VgsConstants.NodeHeight + 1e-5f,
              "the false button stays inside the card's bottom edge");

        Check(PortGeometry.HitBindButton(sensor, trueCenter, aspect, out hit) && hit == PortGroup.Yes,
              "clicking the upper disc binds the yes group");
        Check(PortGeometry.HitBindButton(sensor, falseCenter, aspect, out hit) && hit == PortGroup.No,
              "clicking the lower disc binds the no group");

        // Aspect independence: the button is round on screen, so its distance from the card's
        // right border must not depend on the window's shape.
        float[] aspects = { 0.5625f, 0.75f, 1f, 1.6f };
        for (int i = 0; i < aspects.Length; i++)
        {
            float a = aspects[i];
            Vector2 c;
            Check(PortGeometry.TryBindButton(sensor, PortGroup.Yes, a, out c),
                  "true button at aspect " + a);
            float rightEdge = c.X + VgsConstants.GroupButtonRadius * a;
            CheckClose(rightEdge,
                       sensor.Position.X + VgsConstants.NodeWidth - VgsConstants.PlusButtonRightMargin,
                       "true button right edge is a fixed distance from the border at aspect " + a);
            Check(rightEdge <= sensor.Position.X + VgsConstants.NodeWidth + 1e-5f,
                  "true button stays inside the card at aspect " + a);
        }

        // A block that cannot drive anything has no control at all.
        Node conveyor = MakeTarget(3, "LargeBlockConveyorTube");
        Vector2 unused;
        Check(!PortGeometry.TryBindButton(conveyor, PortGroup.Single, aspect, out unused),
              "no button on a block without a toolbar");
        Check(!PortGeometry.HitBindButton(conveyor, conveyor.Position, aspect, out hit),
              "nothing is clickable on a block without a toolbar");
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

    private static Node WithGridPosition(Node node, Vector3I position)
    {
        // Node.GridPosition is readonly and set in the constructor, so rebuild the node.
        return new Node(node.EntityId, node.SubtypeId, node.DisplayName, position, node.Toolbar);
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
}
