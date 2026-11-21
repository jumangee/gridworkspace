using System;
using System.Collections.Generic;
using VRageMath;

namespace Vgs.Model
{
    /// <summary>
    /// The editable graph for one grid: nodes plus edges, with the invariants the UI and
    /// the future evaluator both rely on.
    ///
    /// Deliberately a plain data structure with no game references. Everything that talks
    /// to Space Engineers lives in <c>Vgs.Game</c>; this type can therefore be exercised in
    /// a test harness that never starts the game, which is the only practical way to test
    /// graph logic given that the in-game feedback loop costs a full game launch.
    ///
    /// Invariants enforced here, not by callers:
    ///  * an edge never starts and ends on the same node;
    ///  * an edge only references ports that exist on both endpoints;
    ///  * an edge occupies a cell of the source's toolbar, and no two edges share a cell;
    ///  * a cell may only be used by a port whose fire group matches its slot;
    ///  * removing a node removes every edge that touched it;
    ///  * a port is drawn only while it carries at least one connection
    ///    (<see cref="LayoutPorts"/>).
    ///
    /// Note what is *not* an invariant: an input port may be the target of several edges.
    /// That is the normal case — several blocks telling the same reactor to switch on — and
    /// it is why the input side of a node has one bar with several lines arriving at it
    /// instead of one bar per line.
    ///
    /// Nor is "one edge per (event, action)" an invariant. The game refuses the same action
    /// twice on one toolbar *page* and accepts it on another page, so two connections that
    /// differ only by their cell are two real, legal connections — and the reason the editor
    /// has to warn when the last page is taken (ADR-0011).
    /// </summary>
    public sealed class GridGraph
    {
        /// <summary>
        /// Hard cap on the number of port bars drawn on one side of one card.
        ///
        /// Not a limit on connections — it is a limit on how far the stack may grow before it
        /// swallows the canvas. A node that exceeds it keeps every edge in the graph; the
        /// surplus ports are simply not drawn (their slot stays negative), which is honest:
        /// the alternative is a bar tower taller than the screen.
        /// </summary>
        public const int MaxPortsPerSide = 6;

        private readonly Dictionary<long, Node> _nodes = new Dictionary<long, Node>();
        private readonly List<Node> _nodeList = new List<Node>();
        private readonly List<Edge> _edges = new List<Edge>();
        private readonly List<Edge> _lines = new List<Edge>();

        /// <summary>Entity id of the grid this graph belongs to. Fixed for the graph's lifetime.</summary>
        public readonly long GridEntityId;

        public GridGraph(long gridEntityId)
        {
            GridEntityId = gridEntityId;
        }

        public int NodeCount { get { return _nodeList.Count; } }
        public int EdgeCount { get { return _edges.Count; } }

        /// <summary>Read-only view of nodes; do not mutate the list itself.</summary>
        public IList<Node> Nodes { get { return _nodeList; } }

        /// <summary>Read-only view of edges; do not mutate the list itself.</summary>
        public IList<Edge> Edges { get { return _edges; } }

        /// <summary>
        /// The edges that get a drawn line: one per distinct (output port, input port) pair.
        ///
        /// The same event may drive the same action on the same block more than once — each
        /// repeat sits in a different toolbar page, because the game refuses a duplicate on one
        /// page ([ADR-0011](../../../../docs/adr/0011-toolbar-is-the-connection.md)). Those
        /// repeats are separate connections and separate toolbar items, but they are the same
        /// line between the same two dots, and drawing it twice would only double the work and
        /// hide the fact that a repeat happened.
        ///
        /// Computed by <see cref="LayoutPorts"/>, which already walks every edge, so the draw
        /// path never has to de-duplicate anything per frame.
        /// </summary>
        public IList<Edge> Lines { get { return _lines; } }

        public Node GetNode(long entityId)
        {
            Node n;
            return _nodes.TryGetValue(entityId, out n) ? n : null;
        }

        public bool Contains(long entityId)
        {
            return _nodes.ContainsKey(entityId);
        }

        /// <summary>
        /// Adds a node, or returns the existing one with the same entity id.
        /// Idempotent so a refresh can call it blindly.
        /// </summary>
        public Node AddNode(Node node)
        {
            if (node == null) return null;

            Node existing;
            if (_nodes.TryGetValue(node.EntityId, out existing)) return existing;

            _nodes.Add(node.EntityId, node);
            _nodeList.Add(node);
            return node;
        }

        /// <summary>
        /// Removes a node and every edge touching it. Returns true when something was
        /// actually removed, so callers can avoid redundant invalidation.
        /// </summary>
        public bool RemoveNode(long entityId)
        {
            Node node;
            if (!_nodes.TryGetValue(entityId, out node)) return false;

            _nodes.Remove(entityId);
            _nodeList.Remove(node);

            for (int i = _edges.Count - 1; i >= 0; i--)
            {
                Edge e = _edges[i];
                if (e.SourceEntityId == entityId || e.TargetEntityId == entityId) _edges.RemoveAt(i);
            }
            return true;
        }

        /// <summary>
        /// Connects an event port of one node to an action port of another, recording the
        /// toolbar cell the item was written into. Returns false and changes nothing when the
        /// edge would be invalid: a self-loop, a port that does not exist, a cell outside the
        /// source's toolbar, a cell already taken, or a cell whose slot belongs to the other
        /// fire group.
        ///
        /// Several edges may share a target port on purpose; see the type comment.
        /// </summary>
        public bool TryConnect(long sourceEntityId, string sourcePortKey, long targetEntityId,
                               string targetPortKey, ToolbarCell cell)
        {
            if (sourceEntityId == targetEntityId) return false;

            Node source = GetNode(sourceEntityId);
            Node target = GetNode(targetEntityId);
            if (source == null || target == null) return false;

            Port outPort = source.FindEvent(sourcePortKey);
            if (outPort == null) return false;
            if (target.FindAction(targetPortKey) == null) return false;

            if (!IsCellValidFor(source, outPort, cell)) return false;
            if (FindEdgeInCell(sourceEntityId, cell) != null) return false;

            _edges.Add(new Edge(sourceEntityId, sourcePortKey, targetEntityId, targetPortKey, cell));
            return true;
        }

        /// <summary>
        /// True when the cell can hold a connection of this port: inside the source's toolbar,
        /// free, and in the slot the port's fire group requires.
        ///
        /// A node without a toolbar (a test fixture, or a block whose toolbar the game did not
        /// expose) accepts no cell at all, which keeps the invariant "every edge has a cell"
        /// true everywhere rather than only in the editor.
        /// </summary>
        public static bool IsCellValidFor(Node source, Port port, ToolbarCell cell)
        {
            if (source == null || port == null || !cell.IsValid) return false;

            ToolbarLayout layout = source.Toolbar;
            if (layout == null || !layout.CanDrive) return false;
            if (cell.ToolbarIndex != layout.ToolbarIndex) return false;
            if (cell.Index < 0 || cell.Index >= layout.CellCount) return false;

            int required = layout.SlotFor(port.Group);
            if (required >= 0 && layout.SlotOf(cell.Index) != required) return false;
            return true;
        }

        /// <summary>The edge that occupies this cell of this source, or null.</summary>
        public Edge FindEdgeInCell(long sourceEntityId, ToolbarCell cell)
        {
            if (!cell.IsValid) return null;
            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];
                if (e.SourceEntityId == sourceEntityId && e.Cell.SameAs(cell)) return e;
            }
            return null;
        }

        /// <summary>True when these two ports are already joined by an edge, in any cell.</summary>
        public bool HasEdge(long sourceEntityId, string sourcePortKey, long targetEntityId, string targetPortKey)
        {
            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];
                if (e.SourceEntityId == sourceEntityId
                    && e.TargetEntityId == targetEntityId
                    && string.Equals(e.SourcePortKey, sourcePortKey, StringComparison.Ordinal)
                    && string.Equals(e.TargetPortKey, targetPortKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Number of edges that end on the given input port.</summary>
        public int CountEdgesToInput(long targetEntityId, string targetPortKey)
        {
            int count = 0;
            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];
                if (e.TargetEntityId == targetEntityId
                    && string.Equals(e.TargetPortKey, targetPortKey, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Number of edges that start at the given output port.</summary>
        public int CountEdgesFromOutput(long sourceEntityId, string sourcePortKey)
        {
            int count = 0;
            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];
                if (e.SourceEntityId == sourceEntityId
                    && string.Equals(e.SourcePortKey, sourcePortKey, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Removes every edge that starts at the given output port and returns how many were
        /// removed. Used when the user clears a source port.
        /// </summary>
        public int RemoveEdgesFromOutput(long sourceEntityId, string sourcePortKey)
        {
            int removed = 0;
            for (int i = _edges.Count - 1; i >= 0; i--)
            {
                Edge e = _edges[i];
                if (e.SourceEntityId == sourceEntityId
                    && string.Equals(e.SourcePortKey, sourcePortKey, StringComparison.Ordinal))
                {
                    _edges.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        public bool RemoveEdge(Edge edge)
        {
            if (edge == null) return false;
            for (int i = 0; i < _edges.Count; i++)
            {
                if (ReferenceEquals(_edges[i], edge)) { _edges.RemoveAt(i); return true; }
            }
            return false;
        }

        public void Clear()
        {
            _nodes.Clear();
            _nodeList.Clear();
            _edges.Clear();
            _lines.Clear();
        }

        /// <summary>
        /// Assigns every port its drawing slot: which stacked bar under the card it occupies.
        ///
        /// This is a *derived* view of the edges, recomputed after any change, and it lives
        /// here rather than in the UI for one reason: the slot of a port depends on the edges
        /// of the whole graph, and only this type owns them. The UI reads <see cref="Port.Slot"/>
        /// and never decides it. The same pass also rebuilds <see cref="Lines"/>.
        ///
        /// Rules, in the order they matter:
        ///
        ///  1. A port with no connection is not drawn at all (slot = -1). A card therefore
        ///     shows only the ports the user actually uses — that is what keeps the canvas
        ///     compact with blocks that expose forty actions.
        ///  2. Slots are taken from zero outwards, so a new bar is appended to the stack
        ///     instead of reshuffling the existing ones.
        ///  3. Slots are assigned in catalog order (the order the ports appear in
        ///     <see cref="Node.Actions"/> / <see cref="Node.Events"/>), which is stable, so the
        ///     same graph always produces the same picture.
        ///  4. An input port shared by several edges is one bar: the first edge to reach it
        ///     gives it a slot and the rest only add lines to its dot.
        ///
        /// Everything is cleared first. A partial reset would let a port keep a slot that
        /// another port has since taken, and the two bars would be drawn on top of each other.
        /// </summary>
        public void LayoutPorts()
        {
            for (int i = 0; i < _nodeList.Count; i++)
            {
                ClearSlots(_nodeList[i].Actions);
                ClearSlots(_nodeList[i].Events);
            }

            _lines.Clear();

            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];

                Node source = GetNode(e.SourceEntityId);
                if (source != null)
                {
                    Port outPort = source.FindEvent(e.SourcePortKey);
                    if (outPort != null) TakeSlot(source.Events, outPort);
                }

                Node target = GetNode(e.TargetEntityId);
                if (target != null)
                {
                    Port inPort = target.FindAction(e.TargetPortKey);
                    if (inPort != null) TakeSlot(target.Actions, inPort);
                }

                if (!HasLineFor(e, i)) _lines.Add(e);
            }
        }

        /// <summary>
        /// True when an edge earlier in the list already draws the same line. Edges are
        /// appended in order, so "earlier" is exactly "was drawn first".
        /// </summary>
        private bool HasLineFor(Edge edge, int index)
        {
            for (int j = 0; j < index; j++)
            {
                Edge other = _edges[j];
                if (other.SourceEntityId != edge.SourceEntityId) continue;
                if (other.TargetEntityId != edge.TargetEntityId) continue;
                if (!string.Equals(other.SourcePortKey, edge.SourcePortKey, StringComparison.Ordinal)) continue;
                if (!string.Equals(other.TargetPortKey, edge.TargetPortKey, StringComparison.Ordinal)) continue;
                return true;
            }
            return false;
        }

        private static void ClearSlots(List<Port> ports)
        {
            for (int i = 0; i < ports.Count; i++) ports[i].Slot = -1;
        }

        /// <summary>Gives the port the lowest free slot, if any is left.</summary>
        private static void TakeSlot(List<Port> ports, Port port)
        {
            if (port.Slot >= 0) return;

            // The occupied set is a bitmask rather than a bool array: this runs for every
            // edge after every graph change, and a per-call array would be an allocation in
            // a path the UI triggers on each connect.
            int used = 0;
            for (int i = 0; i < ports.Count; i++)
            {
                int s = ports[i].Slot;
                if (s >= 0 && s < MaxPortsPerSide) used |= 1 << s;
            }

            for (int s = 0; s < MaxPortsPerSide; s++)
            {
                if ((used & (1 << s)) == 0)
                {
                    port.Slot = s;
                    return;
                }
            }

            // No free slot: the port keeps -1 and is not drawn. Its edges still exist.
        }

        /// <summary>
        /// Seeds readable positions for nodes that have never been placed: a coarse grid
        /// ordered by the block's own grid coordinates, so a freshly opened editor looks
        /// like the ship rather than a pile.
        /// </summary>
        public void LayoutUnplaced()
        {
            const float stepX = 0.26f;
            const float stepY = 0.14f;
            const int columns = 6;

            int placed = 0;
            for (int i = 0; i < _nodeList.Count; i++)
            {
                Node n = _nodeList[i];
                if (n.Position != Vector2.Zero) continue;

                int col = placed % columns;
                int row = placed / columns;
                n.Position = new Vector2(col * stepX, row * stepY);
                placed++;
            }
        }
    }
}
