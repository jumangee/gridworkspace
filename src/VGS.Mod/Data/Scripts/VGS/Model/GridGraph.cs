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
    ///  * an edge never duplicates an existing one;
    ///  * an edge only references ports that exist on both endpoints;
    ///  * removing a node removes every edge that touched it.
    /// </summary>
    public sealed class GridGraph
    {
        private readonly Dictionary<long, Node> _nodes = new Dictionary<long, Node>();
        private readonly List<Node> _nodeList = new List<Node>();
        private readonly List<Edge> _edges = new List<Edge>();

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
        /// Connects two ports. Returns false and changes nothing when the edge would be
        /// invalid: a self-loop, a duplicate, or a missing endpoint.
        /// </summary>
        public bool TryConnect(long sourceEntityId, string sourcePortKey, long targetEntityId, string targetPortKey)
        {
            if (sourceEntityId == targetEntityId) return false;

            Node source = GetNode(sourceEntityId);
            Node target = GetNode(targetEntityId);
            if (source == null || target == null) return false;

            if (source.FindOutput(sourcePortKey) == null) return false;
            if (target.FindInput(targetPortKey) == null) return false;

            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];
                if (e.SourceEntityId == sourceEntityId
                    && e.TargetEntityId == targetEntityId
                    && string.Equals(e.SourcePortKey, sourcePortKey, StringComparison.Ordinal)
                    && string.Equals(e.TargetPortKey, targetPortKey, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            _edges.Add(new Edge(sourceEntityId, sourcePortKey, targetEntityId, targetPortKey));
            return true;
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

        /// <summary>True when the target input already has a connection (inputs are single-source).</summary>
        public bool IsInputConnected(long targetEntityId, string targetPortKey)
        {
            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];
                if (e.TargetEntityId == targetEntityId
                    && string.Equals(e.TargetPortKey, targetPortKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public void Clear()
        {
            _nodes.Clear();
            _nodeList.Clear();
            _edges.Clear();
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
