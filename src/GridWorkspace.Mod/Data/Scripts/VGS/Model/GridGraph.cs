using System;
using System.Collections.Generic;
using VRageMath;

namespace Vgs.Model
{
    /// <summary>
    /// The editable graph for one grid: nodes plus edges, with the invariants the UI and
    /// the future evaluator both rely on.
    ///
    /// A node is either a block or a named block group (ADR-0015). Both live in this type and
    /// both can be the target of an edge; only a block can be its source, because only a block
    /// owns a toolbar to fire from. Groups are kept in a map of their own, keyed by name: a
    /// group is not an entity, so it has no id to key it by.
    ///
    /// Deliberately a plain data structure with no game references. Everything that talks
    /// to Space Engineers lives in <c>Vgs.Game</c>; this type can therefore be exercised in
    /// a test harness that never starts the game, which is the only practical way to test
    /// graph logic given that the in-game feedback loop costs a full game launch.
    ///
    /// Invariants enforced here, not by callers:
    ///  * an edge only references ports that exist on both endpoints;
    ///  * an edge occupies a cell of the source's toolbar, and no two edges share a cell;
    ///  * a cell may only be used by a port whose fire group matches its slot;
    ///  * removing a node removes every edge that touched it;
    ///  * a group exists only while at least one of its members is in the graph — which is
    ///    how "Show in VGS" aggregates for a group: the group is hidden when every member is
    ///    hidden (ADR-0015);
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
    ///
    /// Nor is "an edge to a group is not a self-loop". A block that belongs to the group it
    /// fires is a legal and useful wiring: the game applies the action to every functional
    /// member, the source included, so a timer inside a group can restart the whole group.
    ///
    /// <b>A block may also drive itself</b> (ADR-0036). The connection is an ordinary toolbar
    /// item whose target is the source block, which the game executes like any other item — a
    /// timer can restart itself, a door can close itself — so the graph must accept the edge
    /// rather than invent a rule the game does not have. The same holds for the watch list: a
    /// controller may observe itself, because the engine's own <c>ValidateBlock</c> only asks
    /// for the same logical construct. Drawing is the only part that needs a special case: a
    /// self-connection is routed around its card instead of between two cards.
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
        private readonly Dictionary<string, Node> _groups = new Dictionary<string, Node>(StringComparer.Ordinal);
        private readonly List<Node> _groupList = new List<Node>();
        private readonly List<Edge> _edges = new List<Edge>();
        private readonly List<Edge> _lines = new List<Edge>();
        private readonly List<ConveyorLink> _conveyors = new List<ConveyorLink>();

        /// <summary>Scratch buffer for the group action rule; reused so a refresh allocates nothing.</summary>
        private readonly List<string> _groupActionIds = new List<string>(16);

        /// <summary>
        /// Captions parallel to <see cref="_groupActionIds"/>: the game's names for the surviving
        /// actions (ADR-0037). A group's ports must read like the same block's own ports, and the
        /// name lives on the member's list, not in the id.
        /// </summary>
        private readonly List<string> _groupActionNames = new List<string>(16);


        /// <summary>
        /// Next cell of the automatic layout. Monotonic for the graph's lifetime so that a block
        /// built while the editor is open gets a fresh cell instead of the origin, and so that
        /// removing a node never renumbers the survivors (ADR-0012).
        /// </summary>
        private int _layoutCell;

        /// <summary>Entity id of the grid this graph belongs to. Fixed for the graph's lifetime.</summary>
        public readonly long GridEntityId;

        public GridGraph(long gridEntityId)
        {
            GridEntityId = gridEntityId;
        }

        public int NodeCount { get { return _nodeList.Count; } }
        public int EdgeCount { get { return _edges.Count; } }

        /// <summary>Read-only view of block nodes; do not mutate the list itself.</summary>
        public IList<Node> Nodes { get { return _nodeList; } }

        /// <summary>
        /// Read-only view of group nodes; do not mutate the list itself.
        ///
        /// Separate from <see cref="Nodes"/> because the two are drawn in different passes: a
        /// group is a frame *behind* the cards of its members, so it cannot be part of the
        /// same list the card pass walks.
        /// </summary>
        public IList<Node> Groups { get { return _groupList; } }

        public int GroupCount { get { return _groupList.Count; } }

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

        /// <summary>
        /// The logical conveyor links between the drawn nodes (ADR-0040).
        ///
        /// <b>A third list, not a kind of edge.</b> An <see cref="Edge"/> is a toolbar item: it
        /// has ports and a cell, it is created and removed by the player, and the game executes
        /// it. A conveyor link has none of that — it is derived from the world, read-only, and
        /// never persisted. Folding it into <see cref="Edges"/> would break every invariant that
        /// list enforces (a cell, a port, a toolbar) and every reconcile pass that owns it.
        ///
        /// The list is rebuilt by <c>GridGraphBuilder</c> from the grid's serialised conveyor
        /// lines; the draw path only reads it. It may contain links whose endpoints no longer
        /// have cards for one refresh, which the draw path tolerates by skipping them, exactly
        /// as it does for an edge whose block was just destroyed.
        /// </summary>
        public IList<ConveyorLink> Conveyors { get { return _conveyors; } }

        public int ConveyorCount { get { return _conveyors.Count; } }

        /// <summary>
        /// Drops every conveyor link. Called when the network is hidden or before it is rebuilt.
        /// </summary>
        public void ClearConveyors()
        {
            _conveyors.Clear();
        }

        /// <summary>
        /// Appends one logical conveyor link. The caller owns de-duplication
        /// (<see cref="ConveyorNetwork.Build"/> does it while walking the raw lines), because a
        /// linear scan here would turn a refresh of a dense network into a quadratic pass.
        /// </summary>
        public void AddConveyor(ConveyorLink link)
        {
            if (link == null) return;
            if (link.SourceEntityId <= 0 || link.TargetEntityId <= 0) return;
            if (link.SourceEntityId == link.TargetEntityId) return;

            _conveyors.Add(link);
        }

        public Node GetNode(long entityId)
        {
            Node n;
            return _nodes.TryGetValue(entityId, out n) ? n : null;
        }

        public bool Contains(long entityId)
        {
            return _nodes.ContainsKey(entityId);
        }

        /// <summary>Group node with this name, or null.</summary>
        public Node GetGroup(string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) return null;
            Node n;
            return _groups.TryGetValue(groupName, out n) ? n : null;
        }

        public bool ContainsGroup(string groupName)
        {
            return !string.IsNullOrEmpty(groupName) && _groups.ContainsKey(groupName);
        }

        /// <summary>
        /// True while this node is still part of the graph, whichever kind it is.
        ///
        /// The UI holds node references across refreshes (selection, hover, the open action
        /// menu), and a refresh can drop either a block or a group. Asking by node rather than
        /// by <see cref="Node.EntityId"/> is what keeps those references honest — a group has
        /// no id to ask about.
        /// </summary>
        public bool ContainsNode(Node node)
        {
            if (node == null) return false;
            if (node.IsGroup) return ContainsGroup(node.GroupName);

            Node live;
            return _nodes.TryGetValue(node.EntityId, out live) && ReferenceEquals(live, node);
        }

        /// <summary>Node an edge ends on: the block, or the group it names.</summary>
        public Node GetTargetNode(Edge edge)
        {
            if (edge == null) return null;
            return edge.TargetsGroup ? GetGroup(edge.TargetGroupName) : GetNode(edge.TargetEntityId);
        }

        /// <summary>
        /// Writes one integrity reading into the node's state. False when the graph has no node
        /// for that id, which is normal: a block can be destroyed between the state pass being
        /// scheduled and it running.
        ///
        /// <b>This is the whole write side of the state pass.</b> It touches <see cref="Node.State"/>
        /// and nothing else — no ports, no groups, no frames, no edges — which is what makes the
        /// cheap 2 Hz pass cheap: the expensive derived layout (<see cref="LayoutPorts"/>,
        /// group frames) belongs to the structural pass and must not be re-run twice a second.
        /// The value is clamped by <see cref="BlockState.SetIntegrity"/>, so a caller may pass
        /// whatever the game reported.
        /// </summary>
        public bool ApplyIntegrity(long entityId, float integrity)
        {
            Node node = GetNode(entityId);
            if (node == null) return false;

            node.State.SetIntegrity(integrity);
            return true;
        }

        /// <summary>
        /// Writes what a block's on/off switch says into the node's state (ADR-0035). False when
        /// the graph has no node for that id, which is normal — a block can be destroyed between
        /// the state pass being scheduled and it running.
        ///
        /// <b>Same contract as <see cref="ApplyIntegrity"/>:</b> it touches
        /// <see cref="Node.State"/> and nothing else, so the cheap pass stays cheap and a card's
        /// ports, slots and frame cannot move because a block was switched off. Both values the
        /// game answers are passed in rather than read here, because the model layer never touches
        /// the world (ADR-0003) and because the precedence between them belongs to
        /// <see cref="BlockState.SetPower"/>.
        ///
        /// <b>A node without a switch is not an error.</b> The pass asks about every visible card;
        /// a conveyor tube answers "off" forever, and writing that would be a claim about a block
        /// that has no switch at all. The write is therefore refused, and the caller treats the
        /// refusal as "nothing to do" — the same way it treats a missing node.
        /// </summary>
        public bool ApplySwitch(long entityId, bool enabled, bool functional)
        {
            Node node = GetNode(entityId);
            if (node == null) return false;
            if (!node.CanBeEnabled) return false;

            node.State.SetPower(enabled, functional);
            return true;
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

            // A conveyor link is not an edge, but it ends on the same nodes and must disappear
            // with them: the draw path would skip it anyway, and a list that only grows would
            // outlive the cards it describes (ADR-0040).
            for (int i = _conveyors.Count - 1; i >= 0; i--)
            {
                ConveyorLink c = _conveyors[i];
                if (c.SourceEntityId == entityId || c.TargetEntityId == entityId) _conveyors.RemoveAt(i);
            }
            return true;
        }

        /// <summary>
        /// Creates or updates the group node with this name, and returns it — or null when the
        /// group has to disappear.
        ///
        /// <b>This is where "Show in VGS" aggregates for a group</b> (ADR-0015). The caller
        /// passes only the members that are drawn on the canvas; a group with none of them is
        /// not a group on the schema, so the node is dropped together with its incoming edges.
        /// The block-level flag therefore needs no group-level counterpart: the group is
        /// visible exactly when at least one of its blocks is.
        ///
        /// <paramref name="memberActions"/> is the whole group, not just the drawn members:
        /// the game applies a group action to every functional member, including members that
        /// are hidden or live on a subgrid, so the action list must be computed from all of
        /// them or the editor would offer less than the connection really does.
        ///
        /// The action ports are rebuilt only when the computed id list actually changed, which
        /// keeps the existing <see cref="Port"/> objects alive across refreshes — and with them
        /// the user's selection, hover and open action menu.
        /// </summary>
        public Node SyncGroup(string groupName, List<long> members, IList<GroupMemberActions> memberActions)
        {
            if (string.IsNullOrEmpty(groupName) || members == null || members.Count == 0)
            {
                RemoveGroup(groupName);
                return null;
            }

            Node node = GetGroup(groupName);
            if (node == null)
            {
                node = new Node(groupName, groupName);
                _groups.Add(groupName, node);
                _groupList.Add(node);
            }

            node.DisplayName = groupName;

            node.Members.Clear();
            for (int i = 0; i < members.Count; i++) node.Members.Add(members[i]);

            BlockPortCatalog.ComputeGroupActionIds(memberActions, _groupActionIds, _groupActionNames);
            if (!SamePortKeys(node.Actions, _groupActionIds))
            {
                BlockPortCatalog.FillActions(node, _groupActionIds, _groupActionNames);

                // An action that dropped out of the group's list — the composition changed, so
                // it is no longer the intersection — cannot be executed any more. Its edges go
                // with it: an edge to a port the node does not have would draw nothing on the
                // target and leave a dot with no line on the source. The item stays in the
                // source's toolbar, so restoring the composition brings the line back on the
                // next reconcile pass.
                RemoveEdgesToMissingGroupPorts(groupName);
            }
            return node;
        }

        /// <summary>
        /// Moves every drawn member card of a group by the same offset and returns how many
        /// cards moved. This is what dragging a group by its title strip does (ADR-0016).
        ///
        /// <b>One offset for all members, never a per-card target.</b> The group's promise is
        /// that its blocks belong together, so the operation is a translation of the whole
        /// cluster: each card keeps its position relative to the others, and the frame — which
        /// is derived from the cards — follows without being written to. The alternative,
        /// moving the frame and re-deriving the cards from it, would need a scale factor the
        /// player never asked for and would drift the cards apart the first time a member was
        /// hidden.
        ///
        /// <b>Only drawn members move.</b> <see cref="Node.Members"/> lists exactly the members
        /// present in the graph; a member hidden with "Show in VGS" or living on a subgrid has
        /// no card, so it cannot be dragged and keeps its stored position. That is deliberate:
        /// the canvas shows what moves, and a hidden block reappearing somewhere else would be
        /// a surprise.
        ///
        /// A member that belongs to two groups moves with whichever one is dragged, and the
        /// other group's frame grows or shifts to contain it — the frames are recomputed from
        /// the cards, so nothing is left inconsistent. Returns zero for a non-group node, so a
        /// caller that passes the wrong node changes nothing.
        /// </summary>
        public int OffsetGroupMembers(Node group, Vector2 delta)
        {
            if (group == null || !group.IsGroup) return 0;
            if (delta.X == 0f && delta.Y == 0f) return 0;

            int moved = 0;
            for (int i = 0; i < group.Members.Count; i++)
            {
                Node member = GetNode(group.Members[i]);
                if (member == null || member.IsGroup) continue;

                member.Position = member.Position + delta;
                moved++;
            }
            return moved;
        }

        /// <summary>
        /// Drops the edges that end on a group port the group no longer has.
        ///
        /// Called only when the group's action list actually changed, so the ordinary refresh
        /// costs nothing: a stable group keeps its ports and therefore its edges.
        /// </summary>
        private void RemoveEdgesToMissingGroupPorts(string groupName)
        {
            Node node = GetGroup(groupName);
            if (node == null) return;

            for (int i = _edges.Count - 1; i >= 0; i--)
            {
                Edge e = _edges[i];
                if (!e.TargetsGroup) continue;
                if (!string.Equals(e.TargetGroupName, groupName, StringComparison.Ordinal)) continue;
                if (node.FindAction(e.TargetPortKey) != null) continue;

                _edges.RemoveAt(i);
            }
        }

        /// <summary>
        /// Drops a group node and every edge that ended on it. Returns true when something was
        /// removed. The item stays in the source block's toolbar — the group may simply be
        /// hidden right now — so nothing is written to the world here.
        /// </summary>
        public bool RemoveGroup(string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) return false;

            Node node;
            if (!_groups.TryGetValue(groupName, out node)) return false;

            _groups.Remove(groupName);
            _groupList.Remove(node);

            for (int i = _edges.Count - 1; i >= 0; i--)
            {
                Edge e = _edges[i];
                if (e.TargetsGroup && string.Equals(e.TargetGroupName, groupName, StringComparison.Ordinal))
                {
                    _edges.RemoveAt(i);
                }
            }
            return true;
        }

        private static bool SamePortKeys(IList<Port> ports, IList<string> keys)
        {
            if (ports.Count != keys.Count) return false;
            for (int i = 0; i < keys.Count; i++)
            {
                if (!string.Equals(ports[i].Key, keys[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>
        /// Connects an event port of one node to an action port of a node, recording the toolbar
        /// cell the item was written into. Returns false and changes nothing when the edge would
        /// be invalid: a port that does not exist, a cell outside the source's toolbar, a cell
        /// already taken, or a cell whose slot belongs to the other fire group.
        ///
        /// <b>The two endpoints may be the same block</b> (ADR-0036). The item names the source
        /// as its target, the game fires it like any other, and refusing it here would make the
        /// editor disagree with a toolbar the player can build by hand. The ports still have to
        /// exist and be of opposite directions, which the two lookups below enforce.
        ///
        /// Several edges may share a target port on purpose; see the type comment.
        /// </summary>
        public bool TryConnect(long sourceEntityId, string sourcePortKey, long targetEntityId,
                               string targetPortKey, ToolbarCell cell)
        {
            Node source = GetNode(sourceEntityId);
            Node target = GetNode(targetEntityId);
            if (source == null || target == null) return false;

            return Connect(source, sourcePortKey, target, targetPortKey, cell, null);
        }

        /// <summary>
        /// Connects an event port of a block to an action port of a named group.
        ///
        /// A group is a legal target and an illegal source: the game has a toolbar item that
        /// applies one action to every functional member of a group, but a group has no
        /// toolbar of its own, so nothing can fire from it (ADR-0015). The action must be one
        /// the group's own action list contains — which is the intersection of its members'
        /// actions, not a union — so a connection the game would not execute cannot be made.
        /// </summary>
        public bool TryConnectToGroup(long sourceEntityId, string sourcePortKey, string groupName,
                                      string targetPortKey, ToolbarCell cell)
        {
            Node source = GetNode(sourceEntityId);
            Node target = GetGroup(groupName);
            if (source == null || target == null) return false;

            return Connect(source, sourcePortKey, target, targetPortKey, cell, groupName);
        }

        private bool Connect(Node source, string sourcePortKey, Node target, string targetPortKey,
                             ToolbarCell cell, string groupName)
        {
            Port outPort = source.FindEvent(sourcePortKey);
            if (outPort == null) return false;
            if (target.FindAction(targetPortKey) == null) return false;

            if (!IsCellValidFor(source, outPort, cell)) return false;
            if (FindEdgeInCell(source.EntityId, cell) != null) return false;

            _edges.Add(groupName == null
                ? new Edge(source.EntityId, sourcePortKey, target.EntityId, targetPortKey, cell)
                : Edge.ToGroup(source.EntityId, sourcePortKey, groupName, targetPortKey, cell));
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
                    && !e.TargetsGroup
                    && e.TargetEntityId == targetEntityId
                    && string.Equals(e.SourcePortKey, sourcePortKey, StringComparison.Ordinal)
                    && string.Equals(e.TargetPortKey, targetPortKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True when this block's port is already joined to that group's port.</summary>
        public bool HasEdgeToGroup(long sourceEntityId, string sourcePortKey, string groupName,
                                   string targetPortKey)
        {
            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];
                if (e.SourceEntityId == sourceEntityId
                    && e.TargetsGroup
                    && string.Equals(e.TargetGroupName, groupName, StringComparison.Ordinal)
                    && string.Equals(e.SourcePortKey, sourcePortKey, StringComparison.Ordinal)
                    && string.Equals(e.TargetPortKey, targetPortKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Number of edges that end on the given input port of a block.</summary>
        public int CountEdgesToInput(long targetEntityId, string targetPortKey)
        {
            int count = 0;
            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];
                if (!e.TargetsGroup
                    && e.TargetEntityId == targetEntityId
                    && string.Equals(e.TargetPortKey, targetPortKey, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Number of edges that end on the given input port of any node — the block or the
        /// group. Used by the action menu, which shows the count next to each row without
        /// caring which kind of node the row belongs to.
        /// </summary>
        public int CountEdgesToInput(Node target, string targetPortKey)
        {
            if (target == null) return 0;
            if (!target.IsGroup) return CountEdgesToInput(target.EntityId, targetPortKey);

            int count = 0;
            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];
                if (e.TargetsGroup
                    && string.Equals(e.TargetGroupName, target.GroupName, StringComparison.Ordinal)
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
        /// Copies into <paramref name="into"/> every edge the given port carries: all the edges
        /// leaving an output port (fan-out), or all the edges arriving at an input port
        /// (fan-in).
        ///
        /// A port carries a *set*, not a single connection, and the remove button on a port
        /// therefore has to clear the whole set. The caller collects first and removes second,
        /// because each edge names a cell of a live block's toolbar and the toolbar is the
        /// connection ([ADR-0011](../../../../docs/adr/0011-toolbar-is-the-connection.md)):
        /// removing the edge without clearing its cell would leave wiring in the block that the
        /// next reconcile pass turns back into a line.
        ///
        /// The buffer belongs to the caller and is appended to, not cleared: a click reuses one
        /// list, so removing a port allocates nothing.
        /// </summary>
        public void CollectEdgesForPort(long entityId, string portKey, PortDirection direction,
                                        List<Edge> into)
        {
            if (into == null || string.IsNullOrEmpty(portKey)) return;

            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];

                // A group edge has no block on its target side, so it can only ever be
                // collected from its *source*. Skipping it for the input direction and keeping
                // it for the output one is what makes "clear this output port" remove a line to
                // a group as well.
                if (e.TargetsGroup && direction == PortDirection.Input) continue;

                long endpoint = direction == PortDirection.Output ? e.SourceEntityId : e.TargetEntityId;
                string key = direction == PortDirection.Output ? e.SourcePortKey : e.TargetPortKey;

                if (endpoint != entityId) continue;
                if (!string.Equals(key, portKey, StringComparison.Ordinal)) continue;

                into.Add(e);
            }
        }

        /// <summary>
        /// Same, for either kind of node: a block's port or a group's port. A group has input
        /// ports only, so <paramref name="direction"/> is only meaningful for a block.
        /// </summary>
        public void CollectEdgesForPort(Node node, string portKey, PortDirection direction, List<Edge> into)
        {
            if (node == null || into == null || string.IsNullOrEmpty(portKey)) return;
            if (!node.IsGroup)
            {
                CollectEdgesForPort(node.EntityId, portKey, direction, into);
                return;
            }

            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];
                if (!e.TargetsGroup) continue;
                if (!string.Equals(e.TargetGroupName, node.GroupName, StringComparison.Ordinal)) continue;
                if (!string.Equals(e.TargetPortKey, portKey, StringComparison.Ordinal)) continue;

                into.Add(e);
            }
        }

        /// <summary>
        /// True when this controller already watches that block (ADR-0017).
        ///
        /// The list is a set: the game keeps one subscription per block, so a second "add" of
        /// the same block would be a port the player cannot tell apart from the first.
        /// </summary>
        public bool HasWatch(Node controller, long targetEntityId)
        {
            if (controller == null || controller.Controller == null) return false;
            return controller.Controller.FindWatch(targetEntityId) != null;
        }

        /// <summary>
        /// Adds a block to a controller's watch list and returns false when it cannot be added:
        /// the node is not an event controller, the target is already watched, or the watch
        /// stack is full.
        ///
        /// <b>The controller may watch itself</b> (ADR-0036). The engine's own
        /// <c>MyEventControllerBlock.ValidateBlock</c> asks only for the same logical construct
        /// and the event component subscribes a block the same way whether or not it is the
        /// controller, so a self-watch is a legal vanilla configuration. The graph therefore
        /// does not add a rule of its own; the picture is a bar carrying the controller's own
        /// name, and the draw pass omits the dashed line, which would run from the bar into its
        /// own card.
        ///
        /// <b>The stack is capped at <see cref="MaxPortsPerSide"/> here, not at draw time.</b> A
        /// watch bar that exists in the list but gets no drawing slot has no "x" on screen, and
        /// the bar's button is the only way to remove an entry — the player would be left with a
        /// block they can never detach. Refusing the seventh attach is the honest answer, and it
        /// is the same cap every other port stack obeys.
        ///
        /// The <c>bool</c> is a backstop: the editor checks the same conditions first so it can
        /// say *which* one failed, because this layer has no user-facing strings.
        ///
        /// <b>The graph is the reservation, not the record.</b> What the controller really
        /// watches is the game's selection plus the editor's overlay on the block (ADR-0018);
        /// this method only makes the picture follow. The caller writes the world first and
        /// calls <see cref="RemoveWatch"/> when the world refused, which is why adding here is
        /// cheap and reversible while writing to a block is not.
        ///
        /// Slots are not assigned here: <see cref="LayoutPorts"/> does that, after the change,
        /// like every other port.
        /// </summary>
        public bool TryAddWatch(Node controller, long targetEntityId, string displayName)
        {
            // No controller state means the node is not a controller — that is the flag (ADR-0030).
            if (controller == null || controller.Controller == null) return false;
            if (controller.IsGroup) return false;
            if (targetEntityId == 0) return false;
            if (controller.Controller.FindWatch(targetEntityId) != null) return false;
            if (controller.Controller.IsFull) return false;

            controller.Controller.Watched.Add(new WatchTarget(targetEntityId,
                string.IsNullOrEmpty(displayName) ? targetEntityId.ToString() : displayName));
            return true;
        }

        /// <summary>
        /// Drops a block from a controller's watch list. Returns true when something was
        /// removed. Nothing is written to any block here — the caller owns the world write and
        /// its own rollback (ADR-0017).
        /// </summary>
        public bool RemoveWatch(Node controller, long targetEntityId)
        {
            if (controller == null || controller.Controller == null) return false;

            List<WatchTarget> watched = controller.Controller.Watched;
            for (int i = 0; i < watched.Count; i++)
            {
                if (watched[i].EntityId != targetEntityId) continue;
                watched.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Rebuilds a controller's watch list from the ids the caller has resolved, keeping the
        /// <see cref="WatchTarget"/> objects that survive (ADR-0018).
        ///
        /// <b>The ids are the truth, the list is the view.</b> They come from the game — its own
        /// selection plus the entries the editor added and the game does not store — so this
        /// method only mirrors them: an id that disappeared loses its bar, an id that appeared
        /// gains one, and the order is the caller's order, which keeps the bars stable between
        /// refreshes instead of reshuffling them.
        ///
        /// Reusing the existing objects matters: a hover, a tooltip or an in-flight removal holds
        /// a <see cref="WatchTarget"/> by reference, and replacing them wholesale would make the
        /// highlight jump to whatever took the same slot. The name, unlike the object, is always
        /// re-read — a block renamed in the terminal must be relabelled here, and the caller's
        /// resolver is what decides what to fall back to when the block is gone.
        /// </summary>
        public static void SyncWatched(Node controller, List<long> ids, Func<long, string> nameOf)
        {
            if (controller == null || ids == null) return;
            if (controller.Controller == null) return;

            List<WatchTarget> watched = controller.Controller.Watched;

            // Reuse what is still there. Built as a lookup because the list is short and the
            // comparison is by id: two WatchTarget objects for one block is the defect this
            // method exists to prevent.
            var existing = new Dictionary<long, WatchTarget>(watched.Count);
            for (int i = 0; i < watched.Count; i++)
            {
                existing[watched[i].EntityId] = watched[i];
            }

            var rebuilt = new List<WatchTarget>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                long id = ids[i];
                if (id <= 0) continue;

                string name = nameOf != null ? nameOf(id) : null;
                if (string.IsNullOrEmpty(name)) name = id.ToString();

                WatchTarget target;
                if (existing.TryGetValue(id, out target))
                {
                    existing.Remove(id);
                    target.DisplayName = name;
                    rebuilt.Add(target);
                    continue;
                }

                rebuilt.Add(new WatchTarget(id, name));
            }

            watched.Clear();
            watched.AddRange(rebuilt);
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the union of the ids the game reports and the ids
        /// the editor added, game first, without duplicates (ADR-0018).
        ///
        /// Two sources, and the order says which one is authoritative: what the game reports is
        /// what the controller is actually configured to watch, so those entries keep the order
        /// the player gave them in the terminal. The editor's own additions follow, in the order
        /// they were made.
        /// </summary>
        public static void MergeWatchIds(List<long> gameIds, List<long> overlayIds, List<long> into)
        {
            if (into == null) return;
            into.Clear();

            AddMissing(gameIds, into);
            AddMissing(overlayIds, into);
        }

        /// <summary>
        /// Drops from <paramref name="overlayIds"/> every id the game has adopted into its own
        /// list, and returns true when something was dropped (ADR-0018).
        ///
        /// A block the editor added is not in the game's list — the component-level write the
        /// mod can make does not touch it. But the player can also add that same block in the
        /// terminal, and then the game *does* own it. Keeping it in both places would count it
        /// twice: once as game state and once as an editor addition that no longer needs
        /// replaying after a reload.
        /// </summary>
        public static bool PruneAdoptedWatchIds(List<long> gameIds, List<long> overlayIds)
        {
            if (gameIds == null || overlayIds == null || overlayIds.Count == 0) return false;

            bool changed = false;
            for (int i = overlayIds.Count - 1; i >= 0; i--)
            {
                if (!gameIds.Contains(overlayIds[i])) continue;
                overlayIds.RemoveAt(i);
                changed = true;
            }
            return changed;
        }

        private static void AddMissing(List<long> from, List<long> into)
        {
            if (from == null) return;

            for (int i = 0; i < from.Count; i++)
            {
                long id = from[i];
                if (id <= 0 || into.Contains(id)) continue;
                into.Add(id);
            }
        }

        /// <summary>
        /// Copies the watched entity ids of a controller into a caller-owned list. Used to
        /// write the list to the block and to compare it with what the storage holds.
        /// </summary>
        public static void CollectWatchedIds(Node controller, List<long> into)
        {
            if (into == null) return;
            into.Clear();
            if (controller == null || controller.Controller == null) return;

            List<WatchTarget> watched = controller.Controller.Watched;
            for (int i = 0; i < watched.Count; i++) into.Add(watched[i].EntityId);
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
            _groups.Clear();
            _groupList.Clear();
            _edges.Clear();
            _lines.Clear();
            _conveyors.Clear();
            _layoutCell = 0;
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
                ClearWatchSlots(_nodeList[i]);
            }
            for (int i = 0; i < _groupList.Count; i++)
            {
                // A group has no events, but clearing both lists keeps the pass uniform: a
                // group port that lost its last connection must lose its bar like any other.
                ClearSlots(_groupList[i].Actions);
                ClearSlots(_groupList[i].Events);
                ClearWatchSlots(_groupList[i]);
            }

            _lines.Clear();

            for (int i = 0; i < _nodeList.Count; i++) TakeWatchSlots(_nodeList[i]);

            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];

                Node source = GetNode(e.SourceEntityId);
                if (source != null)
                {
                    Port outPort = source.FindEvent(e.SourcePortKey);
                    if (outPort != null) TakeSlot(source.Events, outPort);
                }

                Node target = GetTargetNode(e);
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
                if (!string.Equals(other.TargetGroupName, edge.TargetGroupName, StringComparison.Ordinal)) continue;
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

        private static void ClearWatchSlots(Node node)
        {
            if (node == null || node.Controller == null) return;

            List<WatchTarget> watched = node.Controller.Watched;
            for (int i = 0; i < watched.Count; i++) watched[i].Slot = -1;
        }

        /// <summary>
        /// Gives every watch target of a controller a drawing slot, in list order.
        ///
        /// Unlike an event or an action port, a watch target needs no "is it connected" test:
        /// being in the list *is* the connection (ADR-0017), so the whole list is drawn. The
        /// cap is the same as for the other stacks — a controller watching more blocks than
        /// fit under its card keeps them all in the graph and the list, and simply does not
        /// draw the surplus, exactly like a surplus port.
        /// </summary>
        private static void TakeWatchSlots(Node node)
        {
            if (node == null || node.Controller == null) return;

            List<WatchTarget> watched = node.Controller.Watched;
            int count = watched.Count < MaxPortsPerSide ? watched.Count : MaxPortsPerSide;
            for (int i = 0; i < count; i++) watched[i].Slot = i;
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
        ///
        /// "Never placed" is <see cref="Node.Placed"/>, not "position is zero": a card the
        /// player parked at the origin is placed, and moving it on the next refresh would be a
        /// defect the player sees immediately (ADR-0012).
        ///
        /// <b>Order is by block position, not by insertion order.</b> These positions are never
        /// saved — only a card the player moved is persisted — so the layout has to be
        /// reproducible from the blocks alone. Iterating the node list would order by whichever
        /// block <c>IMyCubeGrid.GetBlocks</c> happened to return first, and building or grinding
        /// one block would then reshuffle every untouched card between two sessions. Sorting by
        /// grid position (with the entity id as a tie-break, since blocks can share a cell
        /// position in edge cases) makes the picture a function of the ship.
        ///
        /// The sort runs on the unplaced nodes only, so on a settled graph the list is empty and
        /// this costs nothing.
        /// </summary>
        public void LayoutUnplaced()
        {
            const float stepX = 0.26f;
            const float stepY = 0.14f;
            const int columns = 6;

            List<Node> unplaced = null;
            for (int i = 0; i < _nodeList.Count; i++)
            {
                if (_nodeList[i].Placed) continue;
                if (unplaced == null) unplaced = new List<Node>();
                unplaced.Add(_nodeList[i]);
            }
            if (unplaced == null) return;

            // Insertion sort: the list is short (only cards never touched) and this avoids a
            // comparer allocation on a path that runs after every refresh.
            for (int i = 1; i < unplaced.Count; i++)
            {
                Node current = unplaced[i];
                int j = i - 1;
                while (j >= 0 && ComparePlacement(unplaced[j], current) > 0)
                {
                    unplaced[j + 1] = unplaced[j];
                    j--;
                }
                unplaced[j + 1] = current;
            }

            for (int i = 0; i < unplaced.Count; i++)
            {
                // The cell counter is a graph field, not the loop index: a block added while
                // the editor is open must continue after the cards already on the canvas, not
                // start over at the origin and land on top of them. It is also what keeps the
                // numbering stable when a node is removed — survivors keep their cells.
                int cell = _layoutCell++;
                int col = cell % columns;
                int row = cell / columns;
                unplaced[i].Position = new Vector2(col * stepX, row * stepY);
            }
        }

        /// <summary>
        /// Reading order on the construct: grid first, then X, Y, Z, then entity id (ADR-0032).
        ///
        /// <b>The grid comes first, and that is not a tie-break.</b> The graph covers a whole
        /// construct, so blocks of different subgrids arrive interleaved in the terminal system's
        /// order. Sorting by position alone would scatter one subgrid's cards across the canvas,
        /// and the frame drawn around them would be a zigzag covering half the picture — a frame
        /// that says nothing. Keeping one grid's cards contiguous is what makes the frame read as
        /// "these belong together".
        ///
        /// The id is the final tie-break so that two blocks in the same cell (which the grid
        /// allows for some multi-block setups) still get a stable order instead of swapping places
        /// per run.
        /// </summary>
        private static int ComparePlacement(Node a, Node b)
        {
            if (a.GridEntityId != b.GridEntityId) return a.GridEntityId < b.GridEntityId ? -1 : 1;
            if (a.GridPosition.X != b.GridPosition.X) return a.GridPosition.X < b.GridPosition.X ? -1 : 1;
            if (a.GridPosition.Y != b.GridPosition.Y) return a.GridPosition.Y < b.GridPosition.Y ? -1 : 1;
            if (a.GridPosition.Z != b.GridPosition.Z) return a.GridPosition.Z < b.GridPosition.Z ? -1 : 1;
            if (a.EntityId != b.EntityId) return a.EntityId < b.EntityId ? -1 : 1;
            return 0;
        }
    }
}
