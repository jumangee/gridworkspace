using System;
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
    /// other:
    ///
    /// <code>
    ///   +--------------------------+
    ///   | Reactor              (o) |   card: name, subtype, "+"
    ///   | LargeBlockReactor        |
    ///   +--------------------------+
    ///   | Fuel Changed         (o) |   output bar: full width, dot inside at the right
    ///   | Out Of Fuel          (o) |
    ///   +--------------------------+
    ///   | (o)              On Off  |   input bar: full width, dot inside at the left
    ///   +--------------------------+
    /// </code>
    ///
    /// Outputs sit immediately under the card, inputs under them, because the outgoing side
    /// belongs to the block itself while the incoming side is what other blocks do to it.
    ///
    /// <b>Slots, not indices.</b> Which bar a port occupies is <see cref="Port.Slot"/>, which
    /// <see cref="GridGraph.LayoutPorts"/> derives from the edges: a port with no connection
    /// has a negative slot and is not drawn at all. Nothing here computes a slot — this type
    /// only turns one into a rectangle.
    ///
    /// <b>Round things and the aspect ratio.</b> The layout's X and Y units are different
    /// lengths: X spans the window's width, Y its height. A circle therefore cannot be
    /// expressed as "same extent in both axes" — that draws an ellipse. Every method that
    /// returns the centre of a round dot takes the client aspect (height ÷ width) so the dot
    /// lands tangent to its bar's inner edge instead of hanging outside it. The aspect is a
    /// parameter rather than a dependency on the drawing layer, which keeps this type pure and
    /// testable without the game.
    /// </summary>
    public static class PortGeometry
    {
        /// <summary>Card rectangle for a node, in graph units.</summary>
        public static void CardRect(Node node, out Vector2 topLeft, out Vector2 size)
        {
            topLeft = node.Position;
            size = new Vector2(VgsConstants.NodeWidth, VgsConstants.NodeHeight);
        }

        /// <summary>Vertical distance from one port bar to the next in the same stack.</summary>
        public static float PortBarStride
        {
            get { return VgsConstants.PortBarHeight + VgsConstants.PortBarGap; }
        }

        /// <summary>
        /// True when the node shows a "+" button, and where its centre is. The button is the
        /// entry point of bind mode: it exists only for blocks that have events of their own
        /// to offer, so a conveyor tube has no button at all.
        ///
        /// <paramref name="aspectX"/> is needed because the button is round on screen: its
        /// horizontal half-extent is <c>radius * aspectX</c>, and that — not the vertical
        /// radius — is what has to be subtracted from the card's right border. Using the
        /// vertical radius here would make the button's distance from the border depend on the
        /// window's shape, so it would look inset on a wide monitor and flush on a 4:3 one.
        /// </summary>
        public static bool TryPlusButton(Node node, float aspectX, out Vector2 center)
        {
            center = Vector2.Zero;
            if (node == null || !node.CanSourceEvents) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float halfWidth = VgsConstants.PlusButtonRadius * aspectX;
            center = new Vector2(
                node.Position.X + VgsConstants.NodeWidth - VgsConstants.PlusButtonRightMargin - halfWidth,
                node.Position.Y + VgsConstants.NodeHeight * 0.5f);
            return true;
        }

        /// <summary>
        /// Rectangle of the bar for one port, in graph units. Returns false for a port that is
        /// not drawn (no connection, or no free slot).
        ///
        /// Both directions share one width and one left edge, so the bars of a card form a
        /// single flush column under it.
        /// </summary>
        public static bool TryPortBarRect(Node node, Port port, out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;

            if (node == null || port == null || port.Slot < 0) return false;

            float y = node.Position.Y + VgsConstants.NodeHeight + port.Slot * PortBarStride;

            // Input bars sit below the whole output stack, then stack downwards themselves.
            // The separating gap is added only when there *is* a stack above: otherwise the
            // first input bar would float away from the card it belongs to.
            if (port.Direction == PortDirection.Input)
            {
                float outputHeight = OutputStackHeight(node);
                if (outputHeight > 0f) y += outputHeight + VgsConstants.PortStackSpacing;
            }

            topLeft = new Vector2(node.Position.X, y);
            size = new Vector2(VgsConstants.PortBarWidth, VgsConstants.PortBarHeight);
            return true;
        }

        /// <summary>
        /// Centre of the connection dot on a port bar: inside the bar, tangent to the edge the
        /// line uses — the right edge for an output, the left edge for an input.
        ///
        /// <paramref name="aspectX"/> is the client height ÷ width. The dot is a circle on
        /// screen, so its horizontal half-extent is <c>PortDotRadius * aspectX</c>; insetting
        /// the centre by exactly that puts the dot's outer edge on the bar's inner border,
        /// which is the only placement where the bar still contains the dot *and* the line
        /// leaves it with no gap.
        /// </summary>
        public static bool TryPortDot(Node node, Port port, float aspectX, out Vector2 center)
        {
            center = Vector2.Zero;

            Vector2 topLeft, size;
            if (!TryPortBarRect(node, port, out topLeft, out size)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float inset = VgsConstants.PortDotRadius * aspectX;
            float y = topLeft.Y + size.Y * 0.5f;

            center = port.Direction == PortDirection.Output
                ? new Vector2(topLeft.X + size.X - inset, y)
                : new Vector2(topLeft.X + inset, y);
            return true;
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

        /// <summary>Total height a node occupies: card plus both port stacks.</summary>
        public static float TotalHeight(Node node)
        {
            float output = OutputStackHeight(node);
            float input = InputStackHeight(node);

            float total = VgsConstants.NodeHeight + output;
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

            float y = node.Position.Y + VgsConstants.NodeHeight
                      + DrawnPortCount(node, PortDirection.Output) * PortBarStride
                      + VgsConstants.PortBarHeight * 0.5f;

            center = new Vector2(
                node.Position.X + VgsConstants.PortBarWidth
                - VgsConstants.PortDotRadius * aspectX,
                y);
            return true;
        }

        /// <summary>
        /// True when a graph-space point is on this node — its card, or any of its drawn port
        /// bars. Ports belong to the block for hit-testing purposes: clicking a port bar must
        /// select the block rather than fall through to the canvas, otherwise a click on a
        /// connected port would start a pan and the block would feel unselectable.
        /// </summary>
        public static bool HitNode(Node node, Vector2 graphPoint)
        {
            if (node == null) return false;

            Vector2 cardTopLeft, cardSize;
            CardRect(node, out cardTopLeft, out cardSize);
            if (Contains(cardTopLeft, cardSize, graphPoint)) return true;

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

        /// <summary>
        /// True when a graph-space point hits the "+" button. The tolerance is slightly larger
        /// than the drawn radius: a small circle is a hard target with a mouse, and a
        /// near-miss that starts bind mode is much cheaper than a click that does nothing.
        ///
        /// The test is done in a space where the button is actually round. In layout units it
        /// is an ellipse (X and Y units are different lengths), so comparing a plain Euclidean
        /// distance would make the button easy to hit from above and hard to hit from the side.
        /// </summary>
        public static bool HitPlusButton(Node node, Vector2 graphPoint, float aspectX)
        {
            Vector2 center;
            if (!TryPlusButton(node, aspectX, out center)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float r = VgsConstants.PlusButtonRadius * 1.2f;
            float dx = (graphPoint.X - center.X) / aspectX;
            float dy = graphPoint.Y - center.Y;
            return dx * dx + dy * dy <= r * r;
        }
    }
}
