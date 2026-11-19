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
    /// the "read the world" step explicit and testable. Two rules drive the design:
    ///
    ///  1. <b>Cache aggressively, resolve lazily.</b> Terminal blocks are expensive to walk;
    ///     the canvas redraws every frame and must never touch <c>IMyTerminalBlock</c>.
    ///     Everything the renderer needs is copied into plain nodes.
    ///
    ///  2. <b>Never throw into the game loop.</b> A block can be destroyed between two
    ///     frames, an action list can be null, and any exception escaping here would break
    ///     the whole session update. Every read is guarded.
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
        /// ones, and refreshes names. Positions and edges of surviving nodes are preserved,
        /// which is what makes the editor feel stable while the ship is being edited.
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

            VgsLog.Debug("graph refresh: " + graph.NodeCount + " nodes, " + graph.EdgeCount + " edges");
        }

        private static Node CreateNode(IMyTerminalBlock block)
        {
            var node = new Node(
                block.EntityId,
                SafeSubtype(block),
                SafeName(block),
                SafePosition(block));

            var actions = new List<string>();
            node.HasTerminal = TryReadActions(block, actions);
            BlockPortCatalog.FillInputs(node, actions);
            BlockPortCatalog.FillOutputs(node);
            node.Subtitle = BuildSubtitle(node);

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
                BlockPortCatalog.FillInputs(node, actions);
                node.Subtitle = BuildSubtitle(node);
            }
        }

        /// <summary>
        /// Builds the node's subtitle. Lives here rather than in <see cref="Node"/> because it
        /// needs <see cref="VgsText"/>, and the model layer deliberately has no dependency on
        /// anything that touches the game.
        /// </summary>
        private static string BuildSubtitle(Node node)
        {
            if (node.HasTerminal)
            {
                return node.SubtypeId
                    + "  ·  " + VgsText.InputsLabel + " " + node.Inputs.Count
                    + " / " + VgsText.OutputsLabel + " " + node.Outputs.Count;
            }
            return node.SubtypeId + "  ·  " + VgsText.NoPorts;
        }

        private static bool SameActions(Node node, List<string> actions)
        {
            if (node.Inputs.Count != actions.Count) return false;
            for (int i = 0; i < actions.Count; i++)
            {
                if (!string.Equals(node.Inputs[i].Key, actions[i], StringComparison.Ordinal)) return false;
            }
            return true;
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
                // node keeps its subtype and the shared output set, so the user can see it
                // and place it even though it offers no inputs.
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
