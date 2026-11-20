using System;
using System.Collections.Generic;
using VRageMath;
using Vgs.Core;
using Vgs.Model;
using Vgs.Ui;

// Offline checks for the parts of the editor that are pure data and pure geometry: the graph
// invariants, the port-slot layout, the card and port rectangles, and the wire router.
//
// Why this file exists: the in-game feedback loop costs a full game launch, and these are
// exactly the things that decide whether the picture matches what the player did. If the slot
// layout is wrong the bars overlap; if the geometry is wrong the dots and the hit tests
// disagree; if the router is wrong the lines land somewhere else. None of that needs Space
// Engineers, and `Vgs.Model` plus `Vgs.Ui.PortGeometry`/`EdgeRouting` deliberately have no
// game references so this can run without it.
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

    private static Node MakeNode(long id, string subtype, params string[] actions)
    {
        var node = new Node(id, subtype, "block" + id, Vector3I.Zero);
        var list = new List<string>(actions);
        BlockPortCatalog.FillActions(node, list);
        BlockPortCatalog.FillEvents(node);
        node.HasTerminal = list.Count > 0;
        return node;
    }

    public static int Main()
    {
        Console.WriteLine("=== port slots and fan-in ===");
        SlotsAndFanIn();

        Console.WriteLine("=== graph invariants ===");
        Invariants();

        Console.WriteLine("=== card and port geometry ===");
        Geometry();

        Console.WriteLine("=== wire routing ===");
        Routing();

        Console.WriteLine("=== node removal ===");
        Removal();

        Console.WriteLine("=== slot contiguity (input placement depends on it) ===");
        SlotContiguity();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "OFFLINE OK — " + _checks + " checks"
            : "OFFLINE FAILED — " + _failures + " of " + _checks + " checks");

        return _failures == 0 ? 0 : 1;
    }

    private static void SlotsAndFanIn()
    {
        var graph = new GridGraph(1);

        Node timer = MakeNode(10, "LargeBlockTimerBlock", "Start", "Stop", "TriggerNow");
        Node reactor = MakeNode(20, "LargeBlockSmallGenerator", "OnOff", "OnOff_On", "OnOff_Off");
        Node door = MakeNode(30, "LargeBlockDoor", "Open", "Close", "OnOff");
        Node sensor = MakeNode(40, "LargeBlockSensor", "OnOff");

        graph.AddNode(timer);
        graph.AddNode(reactor);
        graph.AddNode(door);
        graph.AddNode(sensor);

        // A timer is a source: it has an event of its own.
        Check(timer.CanSourceEvents, "timer can source events");
        Check(timer.PrimaryEvent != null && timer.PrimaryEvent.Key == "Triggered",
              "timer's primary event is its own Triggered, not the shared StateChanged");

        // A block with no family events cannot source anything.
        Node tube = MakeNode(50, "LargeBlockConveyorTube");
        Check(!tube.CanSourceEvents, "conveyor tube cannot source events (no own events)");
        Check(tube.PrimaryEvent != null && tube.PrimaryEvent.Key == BlockPortCatalog.GenericOutputKey,
              "a block without own events still falls back to the generic event");

        // Nothing connected: no port is drawn at all.
        graph.LayoutPorts();
        Check(DrawnOutputs(timer) == 0 && DrawnInputs(reactor) == 0,
              "unconnected ports are not drawn");

        Check(graph.TryConnect(10, "Triggered", 20, "OnOff"), "timer -> reactor OnOff");
        Check(graph.TryConnect(40, "DetectedChanged", 20, "OnOff"), "sensor -> reactor OnOff (fan-in)");
        Check(graph.TryConnect(10, "Triggered", 30, "Open"), "timer -> door Open (fan-out)");
        graph.LayoutPorts();

        // One output bar per output port, however many edges leave it.
        Check(DrawnOutputs(timer) == 1, "timer draws exactly one output bar for two outgoing edges");
        Check(timer.FindEvent("Triggered").Slot == 0, "timer's Triggered sits in slot 0");

        // One input bar per input port, however many edges arrive at it.
        Check(DrawnInputs(reactor) == 1, "reactor draws exactly one input bar for two incoming edges");
        Check(reactor.FindAction("OnOff").Slot == 0, "reactor's OnOff sits in slot 0");
        Check(graph.CountEdgesToInput(20, "OnOff") == 2, "reactor OnOff reports two sources");

        Check(door.FindAction("Open").Slot == 0, "door's Open sits in slot 0");
        Check(sensor.FindEvent("DetectedChanged").Slot == 0, "sensor's DetectedChanged sits in slot 0");

        // A second connection to a different action of the same block stacks the next bar.
        Check(graph.TryConnect(10, "Triggered", 30, "Close"), "timer -> door Close");
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
        Node b = MakeNode(2, "LargeBlockSmallGenerator", "OnOff");
        graph.AddNode(a);
        graph.AddNode(b);

        Check(!graph.TryConnect(1, "Triggered", 1, "TriggerNow"), "self-loop is rejected");
        Check(!graph.TryConnect(1, "Triggered", 2, "Nope"), "unknown action port is rejected");
        Check(!graph.TryConnect(1, "Nope", 2, "OnOff"), "unknown event port is rejected");
        Check(!graph.TryConnect(1, "Triggered", 99, "OnOff"), "unknown target node is rejected");
        Check(!graph.TryConnect(1, "TriggerNow", 2, "OnOff"),
              "an action cannot be used as a source (directions are not interchangeable)");
        Check(!graph.TryConnect(1, "Triggered", 2, "OnOff_On"),
              "an event cannot be used as a target");

        Check(graph.TryConnect(1, "Triggered", 2, "OnOff"), "valid connection accepted");
        Check(!graph.TryConnect(1, "Triggered", 2, "OnOff"), "duplicate is rejected");
        Check(graph.EdgeCount == 1, "rejected attempts changed nothing");
        Check(graph.HasEdge(1, "Triggered", 2, "OnOff"), "HasEdge sees the connection");

        // Fan-out from one output port to different targets is allowed.
        Node c = MakeNode(3, "LargeBlockDoor", "Open");
        graph.AddNode(c);
        Check(graph.TryConnect(1, "Triggered", 3, "Open"), "one output port may feed several targets");
        Check(graph.EdgeCount == 2, "both edges kept");
    }

    private static void Geometry()
    {
        var graph = new GridGraph(3);
        Node src = MakeNode(1, "LargeBlockTimerBlock", "Start");
        Node dst = MakeNode(2, "LargeBlockSmallGenerator", "OnOff");
        src.Position = new Vector2(0.30f, 0.20f);
        dst.Position = new Vector2(0.10f, 0.50f);
        graph.AddNode(src);
        graph.AddNode(dst);

        Check(graph.TryConnect(1, "Triggered", 2, "OnOff"), "connect for geometry test");
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

        // Dots: output near the right edge, input near the left edge, both *inside* their bar.
        // The horizontal half-extent is aspect-corrected, so the check uses the same aspect the
        // caller passes; 1.0 means "square units", i.e. a 1:1 window.
        const float aspect = 1f;
        float inset = VgsConstants.PortDotRadius * aspect;
        Vector2 outDot, inDot;
        Check(PortGeometry.TryPortDot(src, outPort, aspect, out outDot), "output dot");
        Check(PortGeometry.TryPortDot(dst, inPort, aspect, out inDot), "input dot");
        CheckClose(outDot.X, outTopLeft.X + outSize.X - inset,
                   "output dot is inset from the right edge of its bar");
        CheckClose(inDot.X, inTopLeft.X + inset,
                   "input dot is inset from the left edge of its bar");
        Check(outDot.X + inset <= outTopLeft.X + outSize.X,
              "output dot does not cross the right edge of its bar");
        Check(inDot.X - inset >= inTopLeft.X,
              "input dot does not cross the left edge of its bar");
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
            CheckClose(d.X, outTopLeft.X + outSize.X - VgsConstants.PortDotRadius * a,
                       "output dot inset scales with aspect " + a);
            Check(d.X + VgsConstants.PortDotRadius * a <= outTopLeft.X + outSize.X + 1e-6f,
                  "output dot stays inside its bar at aspect " + a);
            Check(d.X - VgsConstants.PortDotRadius * a >= outTopLeft.X,
                  "output dot does not cross the left edge at aspect " + a);
        }

        Vector2 wideDot;
        Check(PortGeometry.TryPortDot(src, outPort, 2f, out wideDot), "output dot with aspect 2");
        CheckClose(wideDot.X, outTopLeft.X + outSize.X - VgsConstants.PortDotRadius * 2f,
                   "dot inset scales with the aspect");
        Check(wideDot.X < outDot.X, "a wider window pushes the output dot further in");

        // The dot fits vertically inside its bar at every zoom-independent layout: a dot taller
        // than its bar would bleed into the neighbouring port.
        Check(VgsConstants.PortDotRadius * 2f <= VgsConstants.PortBarHeight,
              "dot is no taller than its bar");

        // A node with both stacks: input bars are pushed below the output stack.
        Node both = MakeNode(3, "LargeBlockSensor", "OnOff", "Trigger");
        both.Position = new Vector2(0.6f, 0.1f);
        graph.AddNode(both);
        Check(graph.TryConnect(1, "Triggered", 3, "OnOff"), "timer -> sensor OnOff");
        graph.AddNode(MakeNode(4, "LargeBlockDoor", "Open"));
        Check(graph.TryConnect(3, "DetectedChanged", 4, "Open"), "sensor -> door Open");
        graph.LayoutPorts();

        Port bothOut = both.FindEvent("DetectedChanged");
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
        // The "+" exists only on a block that can source events, and it is on the right.
        Vector2 plusCenter;
        Check(PortGeometry.TryPlusButton(src, aspect, out plusCenter), "timer has a plus button");
        Check(plusCenter.X > src.Position.X + VgsConstants.NodeWidth * 0.8f,
              "plus button sits near the card's right edge");

        // The button is 75% of the card's height and centred vertically in the card.
        CheckClose(VgsConstants.PlusButtonRadius * 2f, VgsConstants.NodeHeight * 0.75f,
                   "plus button diameter is 75% of the card height");
        CheckClose(plusCenter.Y, src.Position.Y + VgsConstants.NodeHeight * 0.5f,
                   "plus button is vertically centred in the card");

        // It stays inside the card's right border: margin + diameter must not exceed the width.
        float plusHalfWidth = VgsConstants.PlusButtonRadius * aspect;
        Check(plusCenter.X + plusHalfWidth <= src.Position.X + VgsConstants.NodeWidth + 1e-5f,
              "plus button does not cross the card's right edge");
        Check(plusCenter.X - plusHalfWidth >= src.Position.X,
              "plus button does not cross the card's left edge");

        // Hit testing is done in a round space: a point offset horizontally by less than the
        // aspect-corrected radius still hits, and a point well outside does not.
        Check(PortGeometry.HitPlusButton(src, plusCenter, aspect), "plus button centre hits");
        Check(PortGeometry.HitPlusButton(
                  src, plusCenter + new Vector2(plusHalfWidth * 0.8f, 0f), aspect),
              "plus button hits slightly to the right of centre");
        Check(!PortGeometry.HitPlusButton(
                  src, plusCenter + new Vector2(plusHalfWidth * 4f, 0f), aspect),
              "plus button misses far to the right");

        // The button must stay inside the card and centred at *any* window shape, and its
        // distance from the right border must not depend on the shape: that was the bug when
        // the horizontal margin was applied in vertical units.
        float[] aspects = { 0.5625f, 0.625f, 0.75f, 1f, 1.6f };
        for (int i = 0; i < aspects.Length; i++)
        {
            float a = aspects[i];
            Vector2 c;
            Check(PortGeometry.TryPlusButton(src, a, out c), "plus button at aspect " + a);
            CheckClose(c.Y, src.Position.Y + VgsConstants.NodeHeight * 0.5f,
                       "plus button vertically centred at aspect " + a);

            float halfW = VgsConstants.PlusButtonRadius * a;
            float rightEdge = c.X + halfW;
            CheckClose(rightEdge,
                       src.Position.X + VgsConstants.NodeWidth - VgsConstants.PlusButtonRightMargin,
                       "plus button right edge is a fixed distance from the border at aspect " + a);
            Check(rightEdge <= src.Position.X + VgsConstants.NodeWidth + 1e-5f,
                  "plus button stays inside the card at aspect " + a);
            Check(c.X - halfW >= src.Position.X,
                  "plus button does not cross the left edge at aspect " + a);
        }

        Node noPlus = MakeNode(5, "LargeBlockConveyorTube");
        Vector2 unused;
        Check(!PortGeometry.TryPlusButton(noPlus, aspect, out unused),
              "no plus on a block with no own events");

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
        Node b = MakeNode(2, "LargeBlockSmallGenerator", "OnOff");
        graph.AddNode(a);
        graph.AddNode(b);
        graph.TryConnect(1, "Triggered", 2, "OnOff");
        graph.LayoutPorts();
        Check(b.FindAction("OnOff").Slot == 0, "target port is drawn while connected");

        Check(graph.RemoveNode(1), "source node removed");
        Check(graph.EdgeCount == 0, "removing a node removes its edges");
        graph.LayoutPorts();
        Check(b.FindAction("OnOff").Slot < 0, "the target's port is no longer drawn");

        // A port shared by several edges keeps its bar until the last edge is gone.
        Node c = MakeNode(3, "LargeBlockSensor");
        graph.AddNode(c);
        graph.TryConnect(3, "DetectedChanged", 2, "OnOff");
        graph.LayoutPorts();
        Check(b.FindAction("OnOff").Slot == 0, "port drawn again after reconnect");
        Check(graph.CountEdgesToInput(2, "OnOff") == 1, "one source");
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
        Node dst = MakeNode(2, "LargeBlockSmallGenerator", "OnOff", "OnOff_On", "OnOff_Off", "Enable");
        graph.AddNode(src);
        graph.AddNode(dst);

        // Three separate inputs on the target, from three separate sources.
        graph.AddNode(MakeNode(3, "LargeBlockSensor"));
        graph.AddNode(MakeNode(4, "LargeBlockBatteryBlock"));
        graph.AddNode(MakeNode(5, "LargeBlockCockpit"));

        Check(graph.TryConnect(1, "Triggered", 2, "OnOff"), "c1");
        Check(graph.TryConnect(3, "DetectedChanged", 2, "OnOff_On"), "c2");
        Check(graph.TryConnect(4, "FullyCharged", 2, "OnOff_Off"), "c3");
        Check(graph.TryConnect(5, "OccupiedChanged", 2, "Enable"), "c4");
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
        Node both = MakeNode(6, "LargeBlockSensor", "OnOff");
        graph.AddNode(both);
        Check(graph.TryConnect(1, "Triggered", 6, "OnOff"), "sensor gets an input");
        Check(graph.TryConnect(6, "DetectedChanged", 2, "OnOff"), "sensor also sources");
        graph.LayoutPorts();

        Vector2 outTop, outSize, inTop, inSize;
        PortGeometry.TryPortBarRect(both, both.FindEvent("DetectedChanged"), out outTop, out outSize);
        PortGeometry.TryPortBarRect(both, both.FindAction("OnOff"), out inTop, out inSize);
        Check(inTop.Y >= outTop.Y + outSize.Y, "input stack clears the output stack");

        // The same card's total height matches the bottom of its lowest bar.
        float lowest = inTop.Y + inSize.Y;
        CheckClose(PortGeometry.TotalHeight(both), lowest - both.Position.Y,
                   "total height equals card plus both stacks");

        // And the same identity must hold for a card with inputs only and one with outputs
        // only: TotalHeight is used for culling, so a mismatch hides a card's lower bars.
        Node inOnly = MakeNode(7, "LargeBlockSmallGenerator", "OnOff");
        inOnly.Position = new Vector2(0.9f, 0.9f);
        graph.AddNode(inOnly);
        Check(graph.TryConnect(1, "Triggered", 7, "OnOff"), "input-only target");

        Node outOnly = MakeNode(8, "LargeBlockTimerBlock");
        outOnly.Position = new Vector2(0.9f, 0.2f);
        graph.AddNode(outOnly);
        graph.AddNode(MakeNode(9, "LargeBlockDoor", "Open"));
        Check(graph.TryConnect(8, "Triggered", 9, "Open"), "output-only source");
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
}

