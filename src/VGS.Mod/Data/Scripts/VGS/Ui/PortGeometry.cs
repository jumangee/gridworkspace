using System;
using System.Collections.Generic;
using VRageMath;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Ui
{
    /// <summary>
    /// Where everything on a block card lives, in graph units.
    ///
    /// This is the single source of truth for the card's geometry, and it exists because two
    /// consumers must agree to the pixel: <see cref="VgsEditorScreen"/> draws the card and
    /// its ports, and hit-tests the pointer against the very same rectangles. Duplicating the
    /// arithmetic in both places is how a port ends up drawn in one spot and clickable in
    /// another — a defect that is invisible in the code and obvious in the game.
    ///
    /// <b>The shape.</b> A card is a label (name + subtype) with a round "+" on its right.
    /// Ports are rectangles as wide as the card, hanging underneath it, flush against each
    /// other. Each bar carries two round controls of the same size, one at each end:
    ///
    /// <code>
    ///   +--------------------------+
    ///   | Reactor              (o) |   card: name, subtype, "+"
    ///   | LargeBlockReactor        |
    ///   +--------------------------+
    ///   | (x) Fuel Changed     (o) |   output bar: dot at the right (lines leave), "x" at the left
    ///   | (x) Out Of Fuel      (o) |
    ///   +--------------------------+
    ///   | (o) On Off           (x) |   input bar: dot at the left (lines arrive), "x" at the right
    ///   +--------------------------+
    /// </code>
    ///
    /// Outputs sit immediately under the card, inputs under them, because the outgoing side
    /// belongs to the block itself while the incoming side is what other blocks do to it.
    ///
    /// <b>The dot and the remove button are mirrors of each other</b> (ADR-0014). Both are 75%
    /// of the bar's height and both stand one four-pixel gap clear of the border on their side.
    /// The dot is where the line attaches; the "x" is the control that deletes the port's
    /// connections. Mirroring them is what keeps the label between them, and it makes the
    /// direction readable from a glance at the bar: the dot is always the side the data moves
    /// towards.
    ///
    /// <b>Slots, not indices.</b> Which bar a port occupies is <see cref="Port.Slot"/>, which
    /// <see cref="GridGraph.LayoutPorts"/> derives from the edges: a port with no connection
    /// has a negative slot and is not drawn at all. Nothing here computes a slot — this type
    /// only turns one into a rectangle.
    ///
    /// <b>Round things and the aspect ratio.</b> The layout's X and Y units are different
    /// lengths: X spans the window's width, Y its height. A circle therefore cannot be
    /// expressed as "same extent in both axes" — that draws an ellipse. Every method that
    /// returns the centre of a round control takes the client aspect (height ÷ width), because
    /// both the control's half-width and its gap from the border are vertical lengths that have
    /// to become horizontal ones. The aspect is a parameter rather than a dependency on the
    /// drawing layer, which keeps this type pure and testable without the game.
    /// </summary>
    public static class PortGeometry
    {
        /// <summary>Card rectangle for a node, in graph units.</summary>
        public static void CardRect(Node node, out Vector2 topLeft, out Vector2 size)
        {
            topLeft = node.Position;
            size = new Vector2(VgsConstants.NodeWidth, VgsConstants.NodeHeight);
        }

        /// <summary>
        /// Rectangle of a node's *body*: the card of a block, the frame of a group.
        ///
        /// Everything that hangs under a node — its port bars, their dots and remove buttons —
        /// is positioned from this rectangle, which is why it exists at all: a group has no
        /// card, and duplicating "where do the bars start" in two places is how a group's ports
        /// would end up drawn on top of its members.
        ///
        /// Returns false for a group whose frame has not been laid out yet (zero size), so a
        /// missing layout pass shows as no bars rather than as bars stacked on the origin.
        /// </summary>
        public static bool TryBodyRect(Node node, out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;
            if (node == null) return false;

            if (!node.IsGroup)
            {
                CardRect(node, out topLeft, out size);
                return true;
            }

            topLeft = node.Position;
            size = node.FrameSize;
            return size.X > 0f && size.Y > 0f;
        }

        /// <summary>
        /// Frame rectangle of a group: the smallest box that contains the cards of its drawn
        /// members and their port stacks, plus the padding and the title strip (ADR-0015).
        ///
        /// The frame is *derived*, never stored as a layout the player can drag: it is a
        /// function of where the member cards are, so moving a member resizes the group. The
        /// caller applies the result with <see cref="Node.SetFrame"/>; keeping the arithmetic
        /// here means the draw pass and the hit test cannot disagree about the box.
        ///
        /// Members that are no longer in the graph are skipped, which is why a group of
        /// destroyed blocks shrinks instead of drawing an empty frame.
        ///
        /// The graph is taken directly rather than a resolver delegate: this runs for every
        /// group on every pointer move while a card is dragged, and a delegate would be
        /// allocated per call for no gain — the graph is the only thing that can answer.
        /// </summary>
        public static bool TryGroupFrame(Node group, GridGraph graph,
                                         out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;
            if (group == null || !group.IsGroup || graph == null) return false;
            if (group.Members.Count == 0) return false;

            bool any = false;
            float minX = 0f, minY = 0f, maxX = 0f, maxY = 0f;

            for (int i = 0; i < group.Members.Count; i++)
            {
                Node member = graph.GetNode(group.Members[i]);
                if (member == null || member.IsGroup) continue;

                Vector2 cardTopLeft, cardSize;
                CardRect(member, out cardTopLeft, out cardSize);

                float right = cardTopLeft.X + cardSize.X;
                float bottom = cardTopLeft.Y + TotalHeight(member);

                if (!any)
                {
                    minX = cardTopLeft.X;
                    minY = cardTopLeft.Y;
                    maxX = right;
                    maxY = bottom;
                    any = true;
                    continue;
                }

                if (cardTopLeft.X < minX) minX = cardTopLeft.X;
                if (cardTopLeft.Y < minY) minY = cardTopLeft.Y;
                if (right > maxX) maxX = right;
                if (bottom > maxY) maxY = bottom;
            }

            if (!any) return false;

            float pad = VgsConstants.GroupFramePadding;
            float title = VgsConstants.GroupTitleHeight;

            topLeft = new Vector2(minX - pad, minY - pad - title);
            size = new Vector2(maxX - minX + pad * 2f, maxY - minY + pad * 2f + title);
            return true;
        }

        /// <summary>
        /// Recomputes the frame of every group in the graph from the current card positions.
        ///
        /// Called by the editor after every build and refresh, never from a draw path: the
        /// frame is geometry, and geometry lives here, but a per-frame recomputation of every
        /// group on a large ship would be wasted work — cards only move when the player drags
        /// one or the graph is refreshed.
        ///
        /// It has to run <em>after</em> <c>GridGraph.LayoutPorts()</c>, because a member with
        /// connected ports is taller than its card and the frame must contain its port stack.
        /// </summary>
        public static void LayoutGroupFrames(GridGraph graph)
        {
            if (graph == null) return;

            IList<Node> groups = graph.Groups;
            for (int i = 0; i < groups.Count; i++) LayoutGroupFrame(graph, groups[i]);
        }

        /// <summary>
        /// Recomputes one group's frame. Used while a card is dragged, where only the groups
        /// that contain that card can have changed — recomputing all of them on every pointer
        /// move would make the cost of a drag grow with the number of groups on the ship.
        /// </summary>
        public static void LayoutGroupFrame(GridGraph graph, Node group)
        {
            if (graph == null || group == null || !group.IsGroup) return;

            Vector2 topLeft, size;
            if (!TryGroupFrame(group, graph, out topLeft, out size)) return;
            group.SetFrame(topLeft, size);
        }

        /// <summary>
        /// Recomputes the frames of the groups this block belongs to. A card that is not a
        /// member of anything costs one null check.
        /// </summary>
        public static void LayoutFramesContaining(GridGraph graph, Node member)
        {
            if (graph == null || member == null || member.IsGroup) return;

            IList<Node> groups = graph.Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                Node group = groups[i];
                if (!ContainsMember(group, member.EntityId)) continue;
                LayoutGroupFrame(graph, group);
            }
        }

        private static bool ContainsMember(Node group, long entityId)
        {
            for (int i = 0; i < group.Members.Count; i++)
            {
                if (group.Members[i] == entityId) return true;
            }
            return false;
        }

        /// <summary>
        /// Title strip of a group's frame: the band along its top edge that carries the name.
        ///
        /// Separated from the frame itself because the strip is the group's only click target —
        /// the interior belongs to the member cards — and the draw pass and the hit test must
        /// agree on it to the pixel, which is the whole reason this type exists.
        /// </summary>
        public static bool TryGroupTitleRect(Node group, out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;
            if (group == null || !group.IsGroup) return false;

            Vector2 frameTopLeft, frameSize;
            if (!TryBodyRect(group, out frameTopLeft, out frameSize)) return false;

            topLeft = frameTopLeft;
            size = new Vector2(frameSize.X, VgsConstants.GroupTitleHeight);
            return true;
        }

        /// <summary>
        /// True when a graph-space point is on a group's title strip — the handle the group is
        /// dragged by (ADR-0016).
        ///
        /// It is the same rectangle the name is drawn in, and it exists as a method of its own
        /// because the hit test and the drag gesture must agree on it to the pixel: a strip
        /// that is drawn in one place and grabbed in another makes the group feel unresponsive
        /// at its edges.
        /// </summary>
        public static bool HitGroupTitle(Node group, Vector2 graphPoint)
        {
            Vector2 titleTopLeft, titleSize;
            if (!TryGroupTitleRect(group, out titleTopLeft, out titleSize)) return false;
            return Contains(titleTopLeft, titleSize, graphPoint);
        }

        /// <summary>Vertical distance from one port bar to the next in the same stack.</summary>
        public static float PortBarStride
        {
            get { return VgsConstants.PortBarHeight + VgsConstants.PortBarGap; }
        }

        /// <summary>
        /// True when the node shows the "true"/"false" pair instead of a single "+": its
        /// toolbar has two fire groups, so a connection has to say which list it joins.
        /// </summary>
        public static bool HasGroupButtons(Node node)
        {
            return node != null && node.CanDriveBlocks && node.Toolbar != null && node.Toolbar.YesNo;
        }

        /// <summary>
        /// True when the node shows a bind button for this group, and where its centre is.
        /// The button is the entry point of bind mode: it exists only for blocks that can
        /// drive other blocks, so a conveyor tube has no button at all.
        ///
        /// A single-group toolbar gets one round "+" centred on the card. A yes/no toolbar
        /// gets two smaller discs stacked on the right edge — "T" above, "F" below — because
        /// the two lists are independent and the user must pick one before the target.
        ///
        /// <paramref name="aspectX"/> is needed because the buttons are round on screen: their
        /// horizontal half-extent is <c>radius * aspectX</c>, and that — not the vertical
        /// radius — is what has to be subtracted from the card's right border. Using the
        /// vertical radius here would make the button's distance from the border depend on the
        /// window's shape, so it would look inset on a wide monitor and flush on a 4:3 one.
        /// </summary>
        public static bool TryBindButton(Node node, PortGroup group, float aspectX, out Vector2 center)
        {
            center = Vector2.Zero;
            if (node == null || !node.CanDriveBlocks) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float rightEdge = node.Position.X + VgsConstants.NodeWidth - VgsConstants.PlusButtonRightMargin;

            if (!HasGroupButtons(node))
            {
                if (group != PortGroup.Single) return false;

                float halfWidth = VgsConstants.PlusButtonRadius * aspectX;
                center = new Vector2(rightEdge - halfWidth,
                                     node.Position.Y + VgsConstants.NodeHeight * 0.5f);
                return true;
            }

            if (group == PortGroup.Single) return false;

            float halfGroupWidth = VgsConstants.GroupButtonRadius * aspectX;
            float centreY = group == PortGroup.Yes
                ? VgsConstants.GroupButtonTrueY
                : VgsConstants.GroupButtonFalseY;

            center = new Vector2(rightEdge - halfGroupWidth,
                                 node.Position.Y + VgsConstants.NodeHeight * centreY);
            return true;
        }

        /// <summary>
        /// Which bind button of this node is under a graph-space point, if any. The tolerance
        /// is slightly larger than the drawn radius: a small circle is a hard target with a
        /// mouse, and a near-miss that starts bind mode is much cheaper than a click that does
        /// nothing.
        ///
        /// The test is done in a space where the button is actually round. In layout units it
        /// is an ellipse (X and Y units are different lengths), so comparing a plain Euclidean
        /// distance would make the button easy to hit from above and hard from the side.
        /// </summary>
        public static bool HitBindButton(Node node, Vector2 graphPoint, float aspectX, out PortGroup group)
        {
            group = PortGroup.Single;
            if (node == null || !node.CanDriveBlocks) return false;

            if (aspectX <= 0f) aspectX = 1f;

            if (!HasGroupButtons(node))
            {
                return Hits(node, PortGroup.Single, graphPoint, aspectX, VgsConstants.PlusButtonRadius);
            }

            // The two discs do not overlap, but the hit tolerance could make them touch, so
            // "true" is tested first and wins a tie — it is the common case.
            if (Hits(node, PortGroup.Yes, graphPoint, aspectX, VgsConstants.GroupButtonRadius))
            {
                group = PortGroup.Yes;
                return true;
            }
            if (Hits(node, PortGroup.No, graphPoint, aspectX, VgsConstants.GroupButtonRadius))
            {
                group = PortGroup.No;
                return true;
            }
            return false;
        }

        private static bool Hits(Node node, PortGroup group, Vector2 graphPoint, float aspectX, float radius)
        {
            Vector2 center;
            if (!TryBindButton(node, group, aspectX, out center)) return false;

            float r = radius * 1.2f;
            float dx = (graphPoint.X - center.X) / aspectX;
            float dy = graphPoint.Y - center.Y;
            return dx * dx + dy * dy <= r * r;
        }

        /// <summary>
        /// Rectangle of the bar for one port, in graph units. Returns false for a port that is
        /// not drawn (no connection, or no free slot).
        ///
        /// Both directions share one width and one left edge, so the bars of a node form a
        /// single flush column under it. The column hangs from the node's <em>body</em> — the
        /// card of a block, the frame of a group — so a group's incoming bars sit under its
        /// members rather than under an imaginary card at the group's origin.
        /// </summary>
        public static bool TryPortBarRect(Node node, Port port, out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;

            if (node == null || port == null || port.Slot < 0) return false;

            Vector2 bodyTopLeft, bodySize;
            if (!TryBodyRect(node, out bodyTopLeft, out bodySize)) return false;

            float y = bodyTopLeft.Y + bodySize.Y + port.Slot * PortBarStride;

            // Input bars sit below the whole output stack, then stack downwards themselves.
            // The separating gap is added only when there *is* a stack above: otherwise the
            // first input bar would float away from the card it belongs to.
            if (port.Direction == PortDirection.Input)
            {
                float outputHeight = OutputStackHeight(node);
                if (outputHeight > 0f) y += outputHeight + VgsConstants.PortStackSpacing;
            }

            topLeft = new Vector2(bodyTopLeft.X, y);
            size = new Vector2(bodySize.X, VgsConstants.PortBarHeight);
            return true;
        }

        /// <summary>
        /// Centre of the connection dot on a port bar: inside the bar, one control-width and
        /// one four-pixel gap clear of the edge the line uses — the right edge for an output,
        /// the left edge for an input.
        ///
        /// <paramref name="aspectX"/> is the client height ÷ width. The dot is a circle on
        /// screen, so its horizontal half-extent is <c>PortControlRadius * aspectX</c>. The gap
        /// is converted the same way, which is what makes it four *screen* pixels instead of
        /// four layout units (see <see cref="VgsConstants.PortControlMargin"/>).
        /// </summary>
        public static bool TryPortDot(Node node, Port port, float aspectX, out Vector2 center)
        {
            center = Vector2.Zero;

            Vector2 topLeft, size;
            if (!TryPortBarRect(node, port, out topLeft, out size)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float offset = ControlOffset(aspectX);
            float y = topLeft.Y + size.Y * 0.5f;

            center = port.Direction == PortDirection.Output
                ? new Vector2(topLeft.X + size.X - offset, y)
                : new Vector2(topLeft.X + offset, y);
            return true;
        }

        /// <summary>
        /// Centre of the remove button on a port bar: the connection dot's mirror image, same
        /// size, on the opposite side of the bar. Clicking it deletes every connection the port
        /// carries (<see cref="VgsEditorScreen.RemovePortConnections"/>).
        ///
        /// It is derived from the same two constants as the dot rather than from its own, so
        /// the two controls cannot drift into different sizes or different gaps — a bar whose
        /// left and right controls disagree looks broken at every zoom.
        /// </summary>
        public static bool TryPortRemoveButton(Node node, Port port, float aspectX, out Vector2 center)
        {
            center = Vector2.Zero;

            Vector2 topLeft, size;
            if (!TryPortBarRect(node, port, out topLeft, out size)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float offset = ControlOffset(aspectX);
            float y = topLeft.Y + size.Y * 0.5f;

            center = port.Direction == PortDirection.Output
                ? new Vector2(topLeft.X + offset, y)
                : new Vector2(topLeft.X + size.X - offset, y);
            return true;
        }

        /// <summary>
        /// The port whose remove button is under a graph-space point, or null.
        ///
        /// Only drawn ports are candidates: a port with no connection has no bar and therefore
        /// no button, which is the same rule that keeps an unconnected port off the screen.
        /// The tolerance is a little larger than the disc, because a small circle is a hard
        /// target with a mouse and a near-miss that deletes nothing is cheap.
        /// </summary>
        public static Port HitPortRemoveButton(Node node, Vector2 graphPoint, float aspectX)
        {
            if (node == null) return null;
            if (aspectX <= 0f) aspectX = 1f;

            Port hit = HitRemoveButton(node.Events, node, graphPoint, aspectX);
            if (hit != null) return hit;
            return HitRemoveButton(node.Actions, node, graphPoint, aspectX);
        }

        private static Port HitRemoveButton(System.Collections.Generic.List<Port> ports, Node node,
                                            Vector2 graphPoint, float aspectX)
        {
            float tolerance = VgsConstants.PortControlRadius * VgsConstants.PortControlHitTolerance;

            for (int i = 0; i < ports.Count; i++)
            {
                Port port = ports[i];
                if (port.Slot < 0) continue;

                Vector2 center;
                if (!TryPortRemoveButton(node, port, aspectX, out center)) continue;

                // Same aspect correction as the bind button: in layout units the disc is an
                // ellipse, so a plain Euclidean distance would make it easy to hit from above
                // and hard from the side.
                float dx = (graphPoint.X - center.X) / aspectX;
                float dy = graphPoint.Y - center.Y;
                if (dx * dx + dy * dy <= tolerance * tolerance) return port;
            }
            return null;
        }

        /// <summary>
        /// Rectangle of a port's label, in graph units: the span between the bar's two round
        /// controls.
        ///
        /// Both controls sit one at each end, so the span is the same for an input and an
        /// output — which is why the label never depends on the port's direction. Returns false
        /// when the controls leave no room, in which case the caller draws no label at all
        /// rather than one that runs underneath a control.
        /// </summary>
        public static bool TryPortLabelRect(Node node, Port port, float aspectX,
                                            out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;

            Vector2 barTopLeft, barSize;
            if (!TryPortBarRect(node, port, out barTopLeft, out barSize)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float inset = ControlOffset(aspectX) + ControlHalfWidth(aspectX)
                          + VgsConstants.PortLabelPadding;
            float width = barSize.X - inset * 2f;
            if (width <= 0f) return false;

            topLeft = new Vector2(barTopLeft.X + inset, barTopLeft.Y);
            size = new Vector2(width, barSize.Y);
            return true;
        }

        /// <summary>Horizontal half-extent of a port control, in layout X units.</summary>
        private static float ControlHalfWidth(float aspectX)
        {
            return VgsConstants.PortControlRadius * aspectX;
        }

        /// <summary>
        /// Top edge that puts a line of text of <paramref name="textHeight"/> on the centre line
        /// of a box starting at <paramref name="boxTop"/> and <paramref name="boxHeight"/> tall.
        ///
        /// Text is positioned by its top edge, so "centred" is not "start at the top": a label
        /// drawn at the box's top edge sits half a line too low, which is exactly the defect the
        /// port label had before ADR-0014. The arithmetic is trivial and that is why it lives
        /// here rather than inline in the draw path — it is the requirement, so it has to be
        /// assertable without the game, and the draw path is the one place it cannot be checked.
        ///
        /// Units are whatever the caller passes (layout units or screen units); only the
        /// relationship matters. A text taller than its box is pinned to the box's top instead of
        /// being pushed above it: the box cannot contain such a line, but the label must at least
        /// start inside its own shape rather than straddle the boundary with the bar above.
        /// </summary>
        public static float CentredTextTop(float boxTop, float boxHeight, float textHeight)
        {
            if (textHeight <= 0f || textHeight >= boxHeight) return boxTop;
            return boxTop + (boxHeight - textHeight) * 0.5f;
        }

        /// <summary>
        /// Distance from a bar's border to the centre of the control on that side, in layout X
        /// units: the four-pixel gap plus the disc's own half-width.
        /// </summary>
        private static float ControlOffset(float aspectX)
        {
            return VgsConstants.PortControlMargin * aspectX + ControlHalfWidth(aspectX);
        }

        /// <summary>
        /// Height of the output stack under a card: the number of drawn output bars times the
        /// stride, or zero when nothing is connected. Used to place the input stack below it.
        /// </summary>
        public static float OutputStackHeight(Node node)
        {
            int count = DrawnPortCount(node, PortDirection.Output);
            if (count == 0) return 0f;
            return count * PortBarStride - VgsConstants.PortBarGap;
        }

        /// <summary>Height of the input stack under a card, for culling and hit-testing.</summary>
        public static float InputStackHeight(Node node)
        {
            int count = DrawnPortCount(node, PortDirection.Input);
            if (count == 0) return 0f;
            return count * PortBarStride - VgsConstants.PortBarGap;
        }

        /// <summary>Total height a node occupies: body plus both port stacks.</summary>
        public static float TotalHeight(Node node)
        {
            Vector2 bodyTopLeft, bodySize;
            if (!TryBodyRect(node, out bodyTopLeft, out bodySize)) return 0f;

            float output = OutputStackHeight(node);
            float input = InputStackHeight(node);

            float total = bodySize.Y + output;
            if (input > 0f)
            {
                if (output > 0f) total += VgsConstants.PortStackSpacing;
                total += input;
            }
            return total;
        }

        /// <summary>Number of bars of one direction that are actually drawn on this node.</summary>
        public static int DrawnPortCount(Node node, PortDirection direction)
        {
            if (node == null) return 0;

            System.Collections.Generic.List<Port> ports =
                direction == PortDirection.Output ? node.Events : node.Actions;

            int count = 0;
            for (int i = 0; i < ports.Count; i++)
            {
                if (ports[i].Slot >= 0) count++;
            }
            return count;
        }

        /// <summary>
        /// Centre of the dot a bind started from this card will connect from.
        ///
        /// Two cases, and getting them apart matters because the line is drawn *before* the
        /// click that creates the connection:
        ///
        ///  * the port is already drawn (the block already has connections) — the dot is
        ///    where the port bar is, and the new line simply joins the existing one;
        ///  * the port is not drawn yet — the dot is where the bar is about to appear, i.e.
        ///    the next free slot.
        ///
        /// In both cases the line must start exactly where the dot ends up, or it visibly
        /// jumps at the moment of the click.
        /// </summary>
        public static bool TryPendingSourceDot(Node node, Port port, float aspectX, out Vector2 center)
        {
            center = Vector2.Zero;
            if (node == null) return false;

            if (port != null && port.Slot >= 0) return TryPortDot(node, port, aspectX, out center);

            if (aspectX <= 0f) aspectX = 1f;

            Vector2 bodyTopLeft, bodySize;
            if (!TryBodyRect(node, out bodyTopLeft, out bodySize)) return false;

            float y = bodyTopLeft.Y + bodySize.Y
                      + DrawnPortCount(node, PortDirection.Output) * PortBarStride
                      + VgsConstants.PortBarHeight * 0.5f;

            center = new Vector2(
                bodyTopLeft.X + bodySize.X - ControlOffset(aspectX),
                y);
            return true;
        }

        /// <summary>
        /// True when a graph-space point is on this node — its card or its title strip, or any
        /// of its drawn port bars. Ports belong to the node for hit-testing purposes: clicking
        /// a port bar must select the node rather than fall through to the canvas, otherwise a
        /// click on a connected port would start a pan and the block would feel unselectable.
        ///
        /// <b>A group is not hit through its whole frame.</b> Only the title strip and the port
        /// bars count; the interior of the frame belongs to the member cards, and the gaps
        /// between them stay canvas. A frame that swallowed every click inside it would make
        /// panning across a grouped cluster impossible. The title strip is also the group's
        /// drag handle (<see cref="HitGroupTitle"/>, ADR-0016) — that is why the strip, and not
        /// the frame, is what the pointer can take hold of.
        /// </summary>
        public static bool HitNode(Node node, Vector2 graphPoint)
        {
            if (node == null) return false;

            if (node.IsGroup)
            {
                if (HitGroupTitle(node, graphPoint)) return true;
            }
            else
            {
                Vector2 cardTopLeft, cardSize;
                CardRect(node, out cardTopLeft, out cardSize);
                if (Contains(cardTopLeft, cardSize, graphPoint)) return true;
            }

            return HitAnyPortBar(node.Events, node, graphPoint)
                || HitAnyPortBar(node.Actions, node, graphPoint);
        }

        private static bool HitAnyPortBar(System.Collections.Generic.List<Port> ports, Node node,
                                          Vector2 graphPoint)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                Vector2 topLeft, size;
                if (!TryPortBarRect(node, ports[i], out topLeft, out size)) continue;
                if (Contains(topLeft, size, graphPoint)) return true;
            }
            return false;
        }

        /// <summary>True when a graph-space point is inside a rectangle.</summary>
        public static bool Contains(Vector2 topLeft, Vector2 size, Vector2 point)
        {
            return point.X >= topLeft.X && point.X <= topLeft.X + size.X
                && point.Y >= topLeft.Y && point.Y <= topLeft.Y + size.Y;
        }
    }
}
