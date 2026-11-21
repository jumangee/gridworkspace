using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Game
{
    /// <summary>
    /// Builds and refreshes a <see cref="GridGraph"/> from a live grid.
    ///
    /// This is the only place that reads game entities to populate the editor, which keeps
    /// the "read the world" step explicit and testable. Three rules drive the design:
    ///
    ///  1. <b>Cache aggressively, resolve lazily.</b> Terminal blocks are expensive to walk;
    ///     the canvas redraws every frame and must never touch <c>IMyTerminalBlock</c>.
    ///     Everything the renderer needs is copied into plain nodes.
    ///
    ///  2. <b>Never throw into the game loop.</b> A block can be destroyed between two
    ///     frames, an action list can be null, and any exception escaping here would break
    ///     the whole session update. Every read is guarded.
    ///
    ///  3. <b>The block's toolbar is the truth.</b> A connection exists because an item sits
    ///     in the source block's toolbar (ADR-0011), so the refresh pass reads the toolbars
    ///     and reconciles the graph with what it finds: an item the player removed in the
    ///     terminal loses its line, an item the player added by hand gains one. That is also
    ///     why the picture survives save and load without a storage format of its own.
    /// </summary>
    public static class GridGraphBuilder
    {
        /// <summary>
        /// Creates a graph for the given grid, or an empty graph when the grid is
        /// unavailable. The caller owns the result.
        /// </summary>
        public static GridGraph Build(IMyCubeGrid grid)
        {
            long gridId = 0;
            if (grid != null)
            {
                try { gridId = grid.EntityId; }
                catch (Exception e) { VgsLog.Throttled("build-gridid", "could not read grid id", e, 600); }
            }

            var graph = new GridGraph(gridId);
            if (grid == null) return graph;

            Refresh(graph, grid);
            return graph;
        }

        /// <summary>
        /// Re-synchronises an existing graph with the grid: adds new blocks, drops destroyed
        /// ones, refreshes names, and reconciles the edges with the blocks' toolbars.
        /// Positions and edges of surviving nodes are preserved, which is what makes the
        /// editor feel stable while the ship is being edited.
        /// </summary>
        public static void Refresh(GridGraph graph, IMyCubeGrid grid)
        {
            if (graph == null || grid == null) return;

            var live = new HashSet<long>();

            try
            {
                var slimBlocks = new List<IMySlimBlock>();
                grid.GetBlocks(slimBlocks);

                int scanned = 0;
                for (int i = 0; i < slimBlocks.Count && scanned < VgsConstants.MaxGridBlocksScanned; i++)
                {
                    IMySlimBlock slim = slimBlocks[i];
                    if (slim == null) continue;
                    scanned++;

                    // A block that is still a construction site has no functional terminal
                    // yet; showing it would offer actions that silently do nothing.
                    IMyCubeBlock fat = slim.FatBlock;
                    if (fat == null) continue;

                    IMyTerminalBlock terminal = fat as IMyTerminalBlock;
                    if (terminal == null) continue;

                    live.Add(terminal.EntityId);

                    Node node = graph.GetNode(terminal.EntityId);
                    if (node == null)
                    {
                        graph.AddNode(CreateNode(terminal));
                    }
                    else
                    {
                        UpdateNode(node, terminal);
                    }
                }
            }
            catch (Exception e)
            {
                VgsLog.Error("GridGraphBuilder.Refresh failed", e);
                return;
            }

            // Drop nodes whose blocks are gone. Collect first: RemoveNode mutates the list.
            var dead = new List<long>();
            IList<Node> nodes = graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!live.Contains(nodes[i].EntityId)) dead.Add(nodes[i].EntityId);
            }
            for (int i = 0; i < dead.Count; i++) graph.RemoveNode(dead[i]);

            graph.LayoutUnplaced();

            ReconcileToolbarEdges(graph);

            // Port bars are a derived view of the edges: a node that just disappeared took
            // its edges with it, so the remaining nodes' stacks have to close up.
            graph.LayoutPorts();

            VgsLog.Debug("graph refresh: " + graph.NodeCount + " nodes, " + graph.EdgeCount + " edges");
        }

        private static Node CreateNode(IMyTerminalBlock block)
        {
            // The toolbar is read once and before the node exists: the game builds it in the
            // block's Init and never resizes it, and the event port list depends on its shape
            // (one fire group or two), so it has to be known before the ports are built.
            ToolbarLayout toolbar = ToolbarProbe.Probe(block);

            var node = new Node(
                block.EntityId,
                SafeSubtype(block),
                SafeName(block),
                SafePosition(block),
                toolbar);

            var actions = new List<string>();
            node.HasTerminal = TryReadActions(block, actions);
            BlockPortCatalog.FillActions(node, actions);
            BlockPortCatalog.FillEvents(node);

            return node;
        }

        private static void UpdateNode(Node node, IMyTerminalBlock block)
        {
            node.DisplayName = SafeName(block);

            // Only rebuild ports when the action set actually changed; rebuilding every
            // refresh would drop the user's port-level selections and churn allocations.
            var actions = new List<string>();
            bool hasTerminal = TryReadActions(block, actions);
            if (hasTerminal != node.HasTerminal || !SameActions(node, actions))
            {
                node.HasTerminal = hasTerminal;
                BlockPortCatalog.FillActions(node, actions);

                // FillActions replaces the Port objects, so the drawing slots computed for the
                // old ones are gone; LayoutPorts (called at the end of Refresh) reassigns them.
            }
        }

        private static bool SameActions(Node node, List<string> actions)
        {
            if (node.Actions.Count != actions.Count) return false;
            for (int i = 0; i < actions.Count; i++)
            {
                if (!string.Equals(node.Actions[i].Key, actions[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>
        /// Rebuilds the edges of every toolbar-driven node from the items actually present in
        /// its toolbar.
        ///
        /// Two directions, and both matter: an edge whose cell no longer holds that item is
        /// removed (the player cleared the slot, or the target block was destroyed), and a
        /// cell that holds a known item without an edge gets one (the player wired it in the
        /// terminal, or the graph was just opened on a saved ship).
        ///
        /// Items that point at a block outside the graph — another grid, a block group — are
        /// left alone: the editor cannot draw them, and deleting them would destroy wiring the
        /// player made by hand.
        /// </summary>
        private static void ReconcileToolbarEdges(GridGraph graph)
        {
            int budget = VgsConstants.MaxToolbarCellsScanned;
            IList<Node> nodes = graph.Nodes;

            for (int n = 0; n < nodes.Count && budget > 0; n++)
            {
                Node node = nodes[n];
                if (!node.CanDriveBlocks || node.Toolbar == null || !node.Toolbar.CanDrive) continue;

                IMyToolbar toolbar = ResolveToolbar(node.EntityId, node.Toolbar.ToolbarIndex);
                if (toolbar == null) continue;
                SyncCells(graph, node, toolbar, ref budget);
            }
        }

        private static void SyncCells(GridGraph graph, Node node, IMyToolbar toolbar, ref int budget)
        {
            ToolbarLayout layout = node.Toolbar;

            // 1. Drop edges whose cell no longer holds that item.
            IList<Edge> edges = graph.Edges;
            for (int i = edges.Count - 1; i >= 0; i--)
            {
                Edge edge = edges[i];
                if (edge.SourceEntityId != node.EntityId) continue;

                long blockId;
                string action;
                bool present = ToolbarWriter.TryDescribe(toolbar, edge.Cell.Index, out blockId, out action);

                if (!present || blockId != edge.TargetEntityId
                    || !string.Equals(action, edge.TargetPortKey, StringComparison.Ordinal))
                {
                    graph.RemoveEdge(edge);
                }
            }

            // 2. Add an edge for every known item that has none.
            for (int index = 0; index < layout.CellCount && budget > 0; index++)
            {
                budget--;

                long blockId;
                string action;
                if (!ToolbarWriter.TryDescribe(toolbar, index, out blockId, out action)) continue;

                var cell = new ToolbarCell(layout.ToolbarIndex, index);
                if (graph.FindEdgeInCell(node.EntityId, cell) != null) continue;

                PortGroup group = ToolbarPlanner.GroupOfCell(layout, index);
                Port port = node.PrimaryEventFor(group);
                if (port == null) continue;

                // TryConnect re-checks that the target node and its action port exist, so an
                // item pointing outside the graph is skipped without a special case here.
                graph.TryConnect(node.EntityId, port.Key, blockId, action, cell);
            }
        }

        /// <summary>
        /// Resolves a node's live toolbar, or null when the block is gone.
        ///
        /// The lookup goes through <see cref="ToolbarProbe.GetToolbar"/> rather than repeating
        /// the component walk: one implementation of "where is this block's toolbar", used by
        /// the probe, the writer and the reconcile pass alike.
        /// </summary>
        private static IMyToolbar ResolveToolbar(long entityId, int toolbarIndex)
        {
            try
            {
                var block = MyAPIGateway.Entities.GetEntityById(entityId) as IMyTerminalBlock;
                if (block == null) return null;

                return ToolbarProbe.GetToolbar(block, toolbarIndex);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("toolbar-resolve", "could not resolve block toolbar", e, 300);
                return null;
            }
        }

        /// <summary>
        /// Reads the terminal actions a block exposes. Returns false when the block has no
        /// terminal at all.
        /// </summary>
        private static bool TryReadActions(IMyTerminalBlock block, List<string> into)
        {
            try
            {
                var actions = new List<Sandbox.ModAPI.Interfaces.ITerminalAction>();
                block.GetActions(actions);
                if (actions == null) return false;

                for (int i = 0; i < actions.Count; i++)
                {
                    var a = actions[i];
                    if (a == null) continue;
                    string id = a.Id;
                    if (!string.IsNullOrEmpty(id)) into.Add(id);
                }
                return into.Count > 0;
            }
            catch (Exception e)
            {
                // A block whose action list cannot be read still belongs on the canvas: the
                // node keeps its subtype, so the user can see it and place it even though it
                // offers no inputs.
                VgsLog.Throttled("getactions", "GetActions failed", e, 300);
                return false;
            }
        }

        private static string SafeSubtype(IMyTerminalBlock block)
        {
            try
            {
                // BlockDefinition is a SerializableDefinitionId struct (never null); its
                // SubtypeName is the canonical subtype id used in save data and in every
                // modding reference.
                string subtype = block.BlockDefinition.SubtypeName;
                if (!string.IsNullOrEmpty(subtype)) return subtype;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("subtype", "could not read block subtype", e, 300);
            }
            return "Unknown";
        }

        private static string SafeName(IMyTerminalBlock block)
        {
            try
            {
                string name = block.CustomName;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch (Exception e)
            {
                // Falls through to the subtype, which is always available.
                VgsLog.Throttled("name", "could not read block name", e, 300);
            }
            return SafeSubtype(block);
        }

        private static Vector3I SafePosition(IMyTerminalBlock block)
        {
            try { return block.Position; }
            catch (Exception e)
            {
                VgsLog.Throttled("position", "could not read block position", e, 300);
                return Vector3I.Zero;
            }
        }
    }
}
