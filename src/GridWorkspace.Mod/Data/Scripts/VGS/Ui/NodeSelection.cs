using System.Collections.Generic;
using VRageMath;
using Vgs.Model;

namespace Vgs.Ui
{
    /// <summary>
    /// The set of nodes the player is working with: what a click, a Shift+click and a marquee
    /// build, what the canvas highlights, and what a drag moves (ADR-0048).
    ///
    /// <b>Why a type and not a <c>Node</c> field on the screen.</b> The editor used to keep one
    /// <see cref="Node"/> reference, and every rule about it was a line in a five-thousand-line
    /// class: "a click replaces it", "a rebuild drops it", "the draw path compares against it".
    /// A set has one more rule of each kind — toggling, marquee selection, and the expansion that
    /// makes a drag move every selected card exactly once — and those are exactly the rules that
    /// are decidable from data, so they live here and are asserted by <c>tools/offline-check.ps1</c>
    /// instead of being judged in the game.
    ///
    /// <b>Identity, not equality.</b> A <see cref="Node"/> is a view-model object that the
    /// refresh updates in place, and a rebuild replaces every one of them (ADR-0023). Membership
    /// is therefore by reference — <see cref="Node"/> overrides neither <c>Equals</c> nor
    /// <c>GetHashCode</c>, so a <see cref="HashSet{T}"/> answers with reference identity, and a
    /// stale object from before a rebuild cannot be mistaken for the live one.
    ///
    /// <b>Nothing here performs an action.</b> The selection is a picture of what the player has
    /// picked, not a target list: every control on the canvas — bind, watch, jump, the power
    /// switch, the manual action menu — acts on the one node under the pointer, and no operation
    /// in this type applies anything to the whole set except a translation of their positions
    /// (<see cref="CollectDragCards"/>). Running an action over a multi-node selection is
    /// deliberately not implemented (ADR-0048 §Decision).
    ///
    /// <b>No allocation in the per-frame paths.</b> <see cref="Contains"/> is a hash lookup, so
    /// the draw path may ask it once per visible card per frame; the two scratch collections are
    /// reused rather than rebuilt, so a drag that expands a group allocates nothing after the
    /// first frame.
    /// </summary>
    public sealed class NodeSelection
    {
        /// <summary>
        /// The selected nodes. Reference identity, because a node is an object the refresh
        /// updates in place and a rebuild replaces (see the type's remarks).
        /// </summary>
        private readonly HashSet<Node> _nodes = new HashSet<Node>();

        /// <summary>
        /// Drawn members of the selected groups, collected while a drag is being prepared.
        ///
        /// A field rather than a local because <see cref="CollectDragCards"/> runs on every drag
        /// start and the screen must not allocate there; it is cleared before and after each use
        /// and nothing outside that method may rely on its contents.
        /// </summary>
        private readonly HashSet<long> _memberIds = new HashSet<long>();

        /// <summary>Nodes found to be stale during a refresh, removed in a second pass.</summary>
        private readonly List<Node> _stale = new List<Node>();

        /// <summary>How many nodes are selected. Zero is the idle canvas.</summary>
        public int Count
        {
            get { return _nodes.Count; }
        }

        /// <summary>True while nothing is selected.</summary>
        public bool IsEmpty
        {
            get { return _nodes.Count == 0; }
        }

        /// <summary>
        /// True when this node is selected. The one question the draw path asks, once per visible
        /// card per frame, which is why membership is a hash lookup rather than a list scan.
        /// </summary>
        public bool Contains(Node node)
        {
            return node != null && _nodes.Contains(node);
        }

        /// <summary>Drops every node. Used by a click on the void, by a rebuild and on close.</summary>
        public void Clear()
        {
            _nodes.Clear();
        }

        /// <summary>
        /// Makes this node the whole selection, or empties the selection when it is null.
        ///
        /// This is what a plain click on a card does when the card was not already part of the
        /// set: the click picks one thing. A click on a card that <em>is</em> selected must not
        /// call this — that would drop the rest of the set just before the drag that needs it.
        /// </summary>
        public void Select(Node node)
        {
            _nodes.Clear();
            if (node != null) _nodes.Add(node);
        }

        /// <summary>Adds a node. False when it was already selected, so a caller can skip a log line.</summary>
        public bool Add(Node node)
        {
            return node != null && _nodes.Add(node);
        }

        /// <summary>
        /// Adds the node if it is not selected and removes it if it is — what a Shift+click does
        /// (ADR-0048 §Decision). Returns true when the node is selected afterwards.
        /// </summary>
        public bool Toggle(Node node)
        {
            if (node == null) return false;
            if (_nodes.Remove(node)) return false;

            _nodes.Add(node);
            return true;
        }

        /// <summary>
        /// Selects every <b>card</b> whose rectangle the given rectangle touches, or adds them to
        /// the current set when <paramref name="additive"/> is true (a Shift+marquee).
        ///
        /// <b>The card, not the node's body.</b> A group's frame is a backdrop drawn around its
        /// members' cards (ADR-0015), so a rectangle that touches one card of a group always
        /// touches the frame too. Selecting the group as well would make every marquee over a
        /// grouped card drag the whole cluster — a gesture the player did not ask for, and one
        /// that cannot be undone without another click. A group is therefore selected only by
        /// Shift+clicking its title strip, which is its one target (ADR-0016). The port bars are
        /// excluded for the same reason: they hang off the card, and a rectangle over a bar whose
        /// card is outside it did not touch a card at all.
        ///
        /// <b>Touching does not count.</b> The test is a strict overlap of two rectangles, the same
        /// "merely touching an edge covers no pixel" rule <see cref="CanvasRect.Overlaps"/> uses,
        /// so a rectangle drawn exactly along a card's border selects nothing rather than
        /// depending on which side of the border the pointer ended up.
        /// </summary>
        public void SelectInRect(GridGraph graph, Vector2 topLeft, Vector2 size, bool additive)
        {
            if (!additive) _nodes.Clear();
            if (graph == null) return;

            IList<Node> nodes = graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                Vector2 cardTopLeft, cardSize;
                PortGeometry.CardRect(nodes[i], out cardTopLeft, out cardSize);
                if (!Overlaps(topLeft, size, cardTopLeft, cardSize)) continue;

                _nodes.Add(nodes[i]);
            }
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the nodes a drag of the current selection moves, in
        /// the graph's own order.
        ///
        /// <b>A selected group contributes its drawn members.</b> A group has no position of its
        /// own — its frame is derived from its members' cards (ADR-0015) — so "move the group"
        /// means "move every card inside it", which is the same rule dragging its title strip
        /// follows (ADR-0016).
        ///
        /// <b>Every card appears exactly once.</b> A card can be selected on its own and also be a
        /// member of a selected group; moving it twice would double its displacement, and a
        /// cluster would come apart in a way no click explains. The expansion is therefore a set,
        /// and the list is built by walking the graph's nodes — so the result is deduplicated and
        /// ordered by the graph rather than by the order the player happened to click.
        ///
        /// A group whose frame has no drawn members contributes nothing, and the list is empty
        /// for an empty selection, so a caller may use the count as "is there anything to drag".
        /// </summary>
        public void CollectDragCards(GridGraph graph, List<Node> into)
        {
            if (into == null) return;

            into.Clear();
            if (graph == null || _nodes.Count == 0) return;

            _memberIds.Clear();

            IList<Node> groups = graph.Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                Node group = groups[i];
                if (!_nodes.Contains(group)) continue;

                List<long> members = group.Members;
                for (int m = 0; m < members.Count; m++) _memberIds.Add(members[m]);
            }

            IList<Node> nodes = graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                if (!_nodes.Contains(node) && !_memberIds.Contains(node.EntityId)) continue;

                into.Add(node);
            }

            _memberIds.Clear();
        }

        /// <summary>
        /// Drops the nodes the refresh has removed, so a selected card that disappeared cannot be
        /// drawn or dragged.
        ///
        /// A refresh deletes the nodes of destroyed blocks and of groups that lost their last
        /// drawn member (ADR-0015); the references stay valid as objects, so without this pass the
        /// canvas would keep an orange border around a card that is no longer there, and a drag
        /// would move a ghost. Collected first and removed afterwards, because a collection may not
        /// be modified while it is being enumerated.
        /// </summary>
        public void RemoveMissing(GridGraph graph)
        {
            if (_nodes.Count == 0) return;

            _stale.Clear();

            foreach (Node node in _nodes)
            {
                if (graph != null && graph.ContainsNode(node)) continue;
                _stale.Add(node);
            }

            for (int i = 0; i < _stale.Count; i++) _nodes.Remove(_stale[i]);

            _stale.Clear();
        }

        /// <summary>
        /// True when two rectangles overlap by more than an edge, in graph units.
        ///
        /// Strict on every side: a box that merely touches another covers no pixel of it, which is
        /// the same rule <see cref="CanvasRect.Overlaps"/> applies to the canvas. Written here
        /// rather than in <see cref="PortGeometry"/> because it is the selection's rule, not the
        /// card's geometry.
        /// </summary>
        private static bool Overlaps(Vector2 aTopLeft, Vector2 aSize, Vector2 bTopLeft, Vector2 bSize)
        {
            if (aSize.X <= 0f || aSize.Y <= 0f) return false;
            if (bSize.X <= 0f || bSize.Y <= 0f) return false;

            if (aTopLeft.X >= bTopLeft.X + bSize.X) return false;
            if (bTopLeft.X >= aTopLeft.X + aSize.X) return false;
            if (aTopLeft.Y >= bTopLeft.Y + bSize.Y) return false;
            if (bTopLeft.Y >= aTopLeft.Y + aSize.Y) return false;

            return true;
        }
    }
}
