using System;
using System.Collections.Generic;
using VRageMath;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Ui
{
    /// <summary>
    /// One control of a card's button row (ADR-0020, ADR-0021).
    ///
    /// The row is the card's whole vocabulary of actions: a button belongs to the *card*, not to
    /// a port, and what it starts depends on the block's shape. Naming the buttons by the gesture
    /// they start — rather than by the glyph they carry — is what lets the icon change (a letter
    /// became a lightbulb) without every call site following it.
    ///
    /// The members are listed in <b>row order</b>, which is also the order they are read in: the
    /// jump button first (ADR-0021), then watch, then the yes/no pair, then the single bind. The
    /// numeric values are not the row positions — <see cref="PortGeometry.CardButtonIndex"/> owns
    /// that — so a new button can be inserted anywhere in the list without renumbering.
    /// </summary>
    public enum CardButtonKind
    {
        /// <summary>
        /// Closes the editor and opens the game's terminal on this card's block (ADR-0021).
        ///
        /// The one button that is not a gesture on the schema: it starts no mode, writes nothing
        /// and changes no connection. It exists because the schema is where a player *finds* a
        /// block, while the block's own settings live in the terminal — so the card carries the
        /// shortest path between the two. Present on every block card and on no group frame: a
        /// group has no terminal of its own.
        /// </summary>
        Jump = 0,

        /// <summary>Starts a bind into the single fire group of a one-group toolbar.</summary>
        BindSingle = 1,

        /// <summary>Starts a bind into the "yes" group of a two-group toolbar.</summary>
        BindYes = 2,

        /// <summary>Starts a bind into the "no" group of a two-group toolbar.</summary>
        BindNo = 3,

        /// <summary>
        /// Starts a watch: the controller will observe the block picked next (ADR-0017). Not a
        /// connection — nothing is written into a toolbar — which is why it is the one button of
        /// the row that is not about firing something.
        /// </summary>
        Watch = 4,
    }

    /// <summary>
    /// Where everything on a block card lives, in graph units.
    ///
    /// This is the single source of truth for the card's geometry, and it exists because two
    /// consumers must agree to the pixel: <see cref="VgsEditorScreen"/> draws the card and
    /// its ports, and hit-tests the pointer against the very same rectangles. Duplicating the
    /// arithmetic in both places is how a port ends up drawn in one spot and clickable in
    /// another — a defect that is invisible in the code and obvious in the game.
    ///
    /// <b>The shape.</b> A card is a label (name + subtype) with its buttons in a horizontal row
    /// along the right edge, vertically centred. Ports are rectangles as wide as the card,
    /// hanging underneath it, flush against each other. Each bar carries two round controls of
    /// the same size, one at each end:
    ///
    /// <code>
    ///   +-------------------------------+
    ///   | Event Controller   (>) (#) (T) (F) |  card: name, subtype, and a right-hand button row
    ///   | EventControllerLarge               |
    ///   +-------------------------------+
    ///   | (x) Fuel Changed          (o) |   output bar: dot at the right (lines leave), "x" at the left
    ///   | (x) Out Of Fuel           (o) |
    ///   +-------------------------------+
    ///   | (o) On Off                (x) |   input bar: dot at the left (lines arrive), "x" at the right
    ///   +-------------------------------+
    /// </code>
    ///
    /// <b>All card buttons live in that row and nowhere else</b> (ADR-0020). Which buttons a card
    /// has follows from what the block is: every block gets the jump button (ADR-0021), a timer
    /// then gets one bind button, a sensor two (one per fire group), an event controller four (the
    /// bell and the two bind buttons after the jump). They share one size and one vertical centre,
    /// so two cards side by side look like two instances of the same control vocabulary instead of
    /// two different designs.
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
            size = new Vector2(VgsConstants.NodeWidth, CardHeight(node));
        }

        /// <summary>
        /// Height of a node's card: the label (name and subtype), plus one line when the node
        /// carries a mode description — which today means an event controller (ADR-0017).
        ///
        /// The height is derived rather than stored because it follows from what the card has
        /// to show, and a stored height would need its own refresh path to stay in step with
        /// the text. Everything positioned from the card — port stacks, the button row, a
        /// group's frame, culling — already goes through <see cref="CardRect"/>, so growing the
        /// card here moves them all at once. The buttons themselves do **not** grow with it:
        /// their diameter comes from the standard card height (ADR-0020).
        /// </summary>
        public static float CardHeight(Node node)
        {
            if (node == null) return VgsConstants.NodeHeight;

            // The third line is reserved for the whole life of a controller, not only while its
            // description is non-empty: a card that grew and shrank as the player picked a
            // different event in the terminal would move its own port stack under the pointer.
            // Having controller state *is* being a controller (ADR-0030), so this is the same
            // test the watch button uses, and the two cannot disagree.
            return VgsConstants.NodeHeight
                   + (node.Controller != null ? VgsConstants.NodeStatusLineHeight : 0f);
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
        /// True when the node shows the "yes"/"no" pair instead of a single bind button: its
        /// toolbar has two fire groups, so a connection has to say which list it joins.
        /// </summary>
        public static bool HasGroupButtons(Node node)
        {
            return node != null && node.CanDriveBlocks && node.Toolbar != null && node.Toolbar.YesNo;
        }

        // ------------------------------------------------------------------ card button row

        /// <summary>
        /// The row's fixed order, left to right: <b>jump → watch → "yes" → "no" → single bind</b>
        /// (ADR-0020, ADR-0021).
        ///
        /// A card shows the subsequence of this list that its block supports, so a given button
        /// keeps its side of the row no matter which other buttons are present, and a button
        /// added in the middle of the row does not renumber the ones after it. The array is the
        /// single place that decides the order: count, index and "which button is at position i"
        /// are all walks over it, which is why adding a button is one entry here plus one branch
        /// in <see cref="HasCardButton"/>.
        ///
        /// Jump is first because it is the button a reader of a card looks for first: it does not
        /// act on the schema, it leaves it for the block itself (ADR-0021).
        /// </summary>
        private static readonly CardButtonKind[] RowOrder =
        {
            CardButtonKind.Jump,
            CardButtonKind.Watch,
            CardButtonKind.BindYes,
            CardButtonKind.BindNo,
            CardButtonKind.BindSingle,
        };

        /// <summary>How many buttons this node's card row shows.</summary>
        public static int CardButtonCount(Node node)
        {
            if (node == null) return 0;

            int count = 0;
            for (int i = 0; i < RowOrder.Length; i++)
            {
                if (HasCardButton(node, RowOrder[i])) count++;
            }
            return count;
        }

        /// <summary>
        /// True when this node's card carries <paramref name="kind"/>. A button exists only where
        /// the gesture it starts can actually happen: the jump button on any block, the watch
        /// button only on a controller, the bind buttons only on a block that drives others, and
        /// the yes/no pair only where the toolbar has two lists.
        ///
        /// The jump button is the only one whose answer does not depend on the toolbar: it needs a
        /// block, because it opens that block's terminal, and nothing else. A group is the one
        /// kind of node without a block behind it, so it is the one kind of node without the
        /// button.
        /// </summary>
        public static bool HasCardButton(Node node, CardButtonKind kind)
        {
            if (node == null) return false;

            switch (kind)
            {
                case CardButtonKind.Jump:
                    return !node.IsGroup;

                case CardButtonKind.Watch:
                    return node.IsEventController;

                case CardButtonKind.BindYes:
                case CardButtonKind.BindNo:
                    return node.CanDriveBlocks && HasGroupButtons(node);

                case CardButtonKind.BindSingle:
                    return node.CanDriveBlocks && !HasGroupButtons(node);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Left-to-right position of a button in its node's row, or -1 when the node does not show
        /// it. The order is fixed — jump, watch, "yes", "no", single bind — so a given button keeps
        /// its side of the row no matter which other buttons the card happens to carry.
        /// </summary>
        public static int CardButtonIndex(Node node, CardButtonKind kind)
        {
            if (node == null) return -1;

            int at = 0;
            for (int i = 0; i < RowOrder.Length; i++)
            {
                CardButtonKind candidate = RowOrder[i];
                if (!HasCardButton(node, candidate)) continue;
                if (candidate == kind) return at;
                at++;
            }
            return -1;
        }

        /// <summary>The kind of the button at <paramref name="index"/>, counting from the left.</summary>
        public static bool TryCardButtonAt(Node node, int index, out CardButtonKind kind)
        {
            kind = CardButtonKind.Jump;
            if (node == null || index < 0) return false;

            int at = 0;
            for (int i = 0; i < RowOrder.Length; i++)
            {
                CardButtonKind candidate = RowOrder[i];
                if (!HasCardButton(node, candidate)) continue;
                if (at == index)
                {
                    kind = candidate;
                    return true;
                }
                at++;
            }
            return false;
        }

        /// <summary>
        /// The bind group a card button starts, if it starts one at all.
        ///
        /// False means the button is not a bind: today that is the watch button (ADR-0017) and the
        /// jump button (ADR-0021). The answer is deliberately not "some group" for those — a
        /// caller that treated the three non-bind buttons alike would start a bind from the bell.
        /// </summary>
        public static bool TryGroupOf(CardButtonKind kind, out PortGroup group)
        {
            switch (kind)
            {
                case CardButtonKind.BindYes:
                    group = PortGroup.Yes;
                    return true;
                case CardButtonKind.BindNo:
                    group = PortGroup.No;
                    return true;
                case CardButtonKind.BindSingle:
                    group = PortGroup.Single;
                    return true;
                default:
                    group = PortGroup.Single;
                    return false;
            }
        }

        /// <summary>
        /// Writes the node's whole row into <paramref name="into"/>, left to right, and returns how
        /// many buttons it has.
        ///
        /// This is the one-pass form of <see cref="CardButtonCount"/> +
        /// <see cref="TryCardButtonAt"/>: the draw path needs every button of every visible card on
        /// every frame, and asking for the count and then for each index walks the order list
        /// <c>n + 1</c> times per card instead of once. Measured on this machine, 400 controller
        /// cards (the <see cref="VgsConstants.MaxRenderedBlocks"/> cap), release build: the
        /// count-then-index form costs 1,27 µs per card, this one with
        /// <see cref="CardButtonRectAt"/> costs 0,35 µs — 0,14 ms of a frame instead of 0,51 ms.
        /// The remaining 0,35 µs is the price of keeping the row's order in one place instead of
        /// duplicating it as a second, faster copy; a cached per-node row would be the same defect
        /// <see cref="PortGeometry"/> exists to prevent.
        ///
        /// <paramref name="into"/> is caller-owned and must have room for
        /// <see cref="MaxCardButtonCount"/> entries; nothing is allocated here, which is what lets
        /// the caller reuse one field buffer for every card of every frame (R010).
        /// </summary>
        public static int GetCardButtons(Node node, CardButtonKind[] into)
        {
            if (node == null || into == null) return 0;

            int at = 0;
            for (int i = 0; i < RowOrder.Length && at < into.Length; i++)
            {
                CardButtonKind candidate = RowOrder[i];
                if (!HasCardButton(node, candidate)) continue;
                into[at] = candidate;
                at++;
            }
            return at;
        }

        /// <summary>
        /// Upper bound of <see cref="GetCardButtons"/>'s output, so a caller can size its buffer
        /// once.
        ///
        /// <b>Derived from the row, not written down.</b> A constant here would be a second copy of
        /// the row's length, and the two would part company on the day a sixth button is added:
        /// <see cref="GetCardButtons"/> would silently truncate the row to the stale bound while
        /// <see cref="CardButtonCount"/> and <see cref="HitCardButton"/> would keep finding the new
        /// button — a button that is clickable but never drawn. Deriving it from
        /// <see cref="RowOrder"/> makes that impossible by construction, and
        /// <c>offline-check</c> pins the agreement with the enum as well.
        /// </summary>
        public static int MaxCardButtonCount
        {
            get { return RowOrder.Length; }
        }

        /// <summary>
        /// Rectangle of the button at <paramref name="index"/> in a row of <paramref name="count"/>
        /// buttons, in graph units.
        ///
        /// The arithmetic is the whole of the row's layout: the row is right-anchored, so a button's
        /// centre is <c>count - 1 - index</c> strides left of the card's right border. Passing the
        /// index and the count in — instead of a <see cref="CardButtonKind"/> — is what lets the draw
        /// loop place each button without looking up where it sits in the row again.
        /// </summary>
        public static void CardButtonRectAt(Node node, int index, int count, float aspectX,
                                            out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;
            if (node == null || count <= 0 || index < 0 || index >= count) return;

            if (aspectX <= 0f) aspectX = 1f;

            float diameter = VgsConstants.CardButtonDiameter;
            float halfWidth = VgsConstants.CardButtonRadius * aspectX;
            float stride = halfWidth * 2f + VgsConstants.CardButtonGap * aspectX;

            float right = node.Position.X + VgsConstants.NodeWidth
                          - VgsConstants.CardButtonMargin * aspectX;
            int fromRight = count - 1 - index;

            float centreX = right - halfWidth - fromRight * stride;
            float centreY = node.Position.Y + CardHeight(node) * 0.5f;

            size = new Vector2(diameter * aspectX, diameter);
            topLeft = new Vector2(centreX - size.X * 0.5f, centreY - size.Y * 0.5f);
        }

        /// <summary>
        /// Rectangle of one button of a node's card row, in graph units, or false when the node
        /// does not show that button.
        ///
        /// <b>The row.</b> Every card control — the jump button, the bind button, the yes/no pair
        /// and the controller's watch button — sits in one horizontal row along the card's right
        /// edge, vertically centred on the card (ADR-0020, ADR-0021). The row is right-anchored:
        /// buttons are packed from the card's right border leftwards, so a card with one button
        /// keeps it exactly where the "+" has always been, and a card with four grows leftwards.
        ///
        /// <b>One size for all of them.</b> The diameter comes from
        /// <see cref="VgsConstants.CardButtonDiameter"/> — a fraction of the *standard* card
        /// height, not of this card's own height. A controller's card is one line taller because
        /// it carries a description, and its buttons must not grow with it: two cards side by
        /// side showing two button sizes is the state this row replaced.
        ///
        /// <paramref name="aspectX"/> is the client height ÷ width. A button is round on screen,
        /// so its horizontal half-extent is <c>radius * aspectX</c>; the gaps and the margin from
        /// the card's border go through the same correction, which is what makes them the same
        /// number of screen pixels on a 16:9 and on a 4:3 window.
        ///
        /// This is the by-kind form, for the callers that have a kind in hand (hit-testing, the
        /// tests). The draw path uses <see cref="GetCardButtons"/> +
        /// <see cref="CardButtonRectAt"/>, which do not have to search the row at all.
        /// </summary>
        public static bool TryCardButtonRect(Node node, CardButtonKind kind, float aspectX,
                                             out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;

            int index = CardButtonIndex(node, kind);
            if (index < 0) return false;

            CardButtonRectAt(node, index, CardButtonCount(node), aspectX, out topLeft, out size);
            return true;
        }

        /// <summary>Centre of one button of a node's card row, in graph units.</summary>
        public static bool TryCardButton(Node node, CardButtonKind kind, float aspectX,
                                         out Vector2 center)
        {
            center = Vector2.Zero;

            Vector2 topLeft, size;
            if (!TryCardButtonRect(node, kind, aspectX, out topLeft, out size)) return false;

            center = topLeft + size * 0.5f;
            return true;
        }

        /// <summary>
        /// Rectangle covering the whole button row of a node, in graph units. False when the card
        /// carries no buttons.
        ///
        /// It exists for the one consumer that has to know where the row *ends* rather than where
        /// a button is: the card's text is clipped to the span left of it, so a long block name
        /// can neither run under a button nor spill over the card.
        /// </summary>
        public static bool TryCardButtonRow(Node node, float aspectX,
                                            out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;

            int count = CardButtonCount(node);
            if (count == 0) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float diameter = VgsConstants.CardButtonDiameter;
            float width = count * diameter * aspectX
                          + (count - 1) * VgsConstants.CardButtonGap * aspectX;

            float right = node.Position.X + VgsConstants.NodeWidth
                          - VgsConstants.CardButtonMargin * aspectX;

            size = new Vector2(width, diameter);
            topLeft = new Vector2(right - width,
                                  node.Position.Y + CardHeight(node) * 0.5f - diameter * 0.5f);
            return true;
        }

        /// <summary>
        /// Which button of this node's row is under a graph-space point, if any.
        ///
        /// The tolerance is larger than the drawn button: a small target is hard with a mouse, and
        /// a near-miss that starts the gesture is much cheaper than a click that does nothing. At
        /// that tolerance the targets of one row overlap, so the *nearest* centre wins rather than
        /// the first one tested: a click in the gap between "yes" and "no" then lands on the
        /// closer of the two instead of on whichever the loop reached first.
        ///
        /// The test is done in a space where the button is actually round. In layout units it is
        /// an ellipse — X and Y units are different lengths — so a plain Euclidean distance would
        /// make the button easy to hit from above and hard from the side.
        /// </summary>
        public static bool HitCardButton(Node node, Vector2 graphPoint, float aspectX,
                                        out CardButtonKind kind)
        {
            kind = CardButtonKind.BindSingle;

            int count = CardButtonCount(node);
            if (count == 0) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float limit = VgsConstants.CardButtonRadius * VgsConstants.CardButtonHitTolerance;
            float best = limit * limit;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                CardButtonKind candidate;
                if (!TryCardButtonAt(node, i, out candidate)) continue;

                Vector2 center;
                if (!TryCardButton(node, candidate, aspectX, out center)) continue;

                float dx = (graphPoint.X - center.X) / aspectX;
                float dy = graphPoint.Y - center.Y;
                float distance = dx * dx + dy * dy;

                // "Not farther" rather than "nearer": an exact tie goes to the button tested
                // last, which is the one further right. The rule only has to be *fixed* — a
                // click in the gap must not depend on the order the row happens to be walked.
                if (distance > best) continue;

                best = distance;
                kind = candidate;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Centre of the watch button, or false when the node is not a controller. The watch line
        /// starts here while the player is choosing a block (ADR-0017): the port does not exist
        /// yet, so the point the line leaves from is the button the player pressed.
        /// </summary>
        public static bool TryWatchButton(Node node, float aspectX, out Vector2 center)
        {
            return TryCardButton(node, CardButtonKind.Watch, aspectX, out center);
        }

        /// <summary>
        /// Horizontal span of a card's text, in graph units: from the card's inner padding to the
        /// left edge of the button row, less one gap.
        ///
        /// It exists so the two consumers cannot disagree about it: the draw pass clips the name,
        /// the subtype and the status line to this span, and the offline gate asserts that the
        /// span is wide enough to be a label rather than a stub. Deriving it in the draw pass
        /// from the row rectangle is exactly how the gap ends up written in the wrong axis — the
        /// gap is a vertical length and has to go through the aspect correction like every other
        /// horizontal gap in this type.
        ///
        /// Returns false when the card has no buttons (the whole card is text, so there is no
        /// span to compute) or when the row leaves no room at all.
        /// </summary>
        public static bool TryCardTextSpan(Node node, float aspectX, out float left, out float right)
        {
            left = 0f;
            right = 0f;

            Vector2 rowTopLeft, rowSize;
            if (!TryCardButtonRow(node, aspectX, out rowTopLeft, out rowSize)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            left = node.Position.X + VgsConstants.NodeWidth * CardTextPaddingRatio;
            right = rowTopLeft.X - VgsConstants.CardButtonTextGap * aspectX;
            return right > left;
        }

        /// <summary>
        /// Rectangle of the damage band on a card, in graph units, or false when the block is not
        /// damaged and there is nothing to draw.
        ///
        /// <b>The band is on the right and its width is the damage, not the health.</b> Integrity
        /// 70% leaves 30% of the card red; 50% leaves half; a block welded full leaves nothing.
        /// Anchoring it to the right edge is what makes the picture read without a legend: the
        /// card is "eaten away" from one side, and a fuller card is a healthier block. Sizing it
        /// by integrity instead would grow the red as the block gets better, which is the same
        /// number saying the opposite thing.
        ///
        /// <b>Full card height, the card's own rectangle.</b> Not the port bars under it: those
        /// are what the block *does*, the card is what the block *is*, and the band answers a
        /// question about the block. Insetting it from the border would leave a dark sliver
        /// between two reds at the seam of a neighbouring card and read as a gap rather than as a
        /// fill.
        ///
        /// The tolerance is <see cref="BlockState.IsDamaged"/>'s, not a second one: the draw pass
        /// must not disagree with the rest of the code about whether a block counts as damaged.
        /// </summary>
        public static bool TryDamageRect(Node node, out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;

            if (node == null || node.IsGroup) return false;
            if (node.State == null || !node.State.IsDamaged) return false;

            float damage = 1f - node.State.Integrity;
            if (damage <= 0f) return false;
            if (damage > 1f) damage = 1f;

            float width = VgsConstants.NodeWidth * damage;

            CardRect(node, out topLeft, out size);
            topLeft = new Vector2(topLeft.X + VgsConstants.NodeWidth - width, topLeft.Y);
            size = new Vector2(width, size.Y);
            return true;
        }

        /// <summary>
        /// Keeps a damage band inside its card and gives it a floor of <paramref name="minWidth"/>
        /// so a barely-scratched block still shows something. All arguments and the result are in
        /// graph units; the caller converts the pixel floor into graph units, because only the
        /// drawing layer knows how big a pixel is.
        ///
        /// <b>Why this exists as a separate step.</b> The band is derived from a fraction, so at a
        /// low zoom a 1% scratch is a fraction of a pixel and a sprite that thin is not reliably
        /// rasterised at all. The floor is therefore a rendering limit, not a meaning: it may
        /// exaggerate damage by a pixel, and it must never hide it — rounding *down* would make a
        /// lightly damaged block indistinguishable from a pristine one, which is the one error
        /// this indicator cannot afford.
        ///
        /// <b>The band is re-derived from the card, never trusted.</b> Width is clamped to the
        /// card and the left edge is recomputed from the right edge, so the band is always flush
        /// with the card's right side and can never spill outside it. That is not defensive
        /// padding: the first version of this code compared a normalized width against the number
        /// 1 (the whole screen) instead of against one pixel, and every damaged card painted a
        /// band a full screen wide. The rule is now in one place, in graph units, and the offline
        /// gate asserts it.
        /// </summary>
        public static void FitDamageBand(ref Vector2 topLeft, ref Vector2 size,
                                         Vector2 cardTopLeft, Vector2 cardSize, float minWidth)
        {
            if (size.X <= 0f || cardSize.X <= 0f) return;

            float width = size.X;
            if (minWidth > 0f && width < minWidth) width = minWidth;
            if (width > cardSize.X) width = cardSize.X;

            topLeft = new Vector2(cardTopLeft.X + cardSize.X - width, cardTopLeft.Y);
            size = new Vector2(width, cardSize.Y);
        }

        /// <summary>
        /// Left padding of a card's text, as a fraction of the card's width. A fraction rather than
        /// a constant because it has to scale with the card: the card is drawn wider than it is
        /// tall, and a padding written in the layout's vertical units would look inset on a narrow
        /// window and flush on a wide one.
        /// </summary>
        public const float CardTextPaddingRatio = 0.05f;

        /// <summary>
        /// Rectangle of the bar for one watch target, in graph units (ADR-0017). False when the
        /// bar is not drawn (slot negative: the controller watches more blocks than fit under
        /// its card).
        ///
        /// The watch stack sits under the input stack, so the card's own actions stay the first
        /// thing under it. Every stack is flush with the one above, which is what makes the
        /// column read as one node rather than as a card with boxes hanging off it.
        /// </summary>
        public static bool TryWatchBarRect(Node node, WatchTarget watch, out Vector2 topLeft,
                                           out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;
            if (node == null || watch == null || watch.Slot < 0) return false;

            Vector2 bodyTopLeft, bodySize;
            if (!TryBodyRect(node, out bodyTopLeft, out bodySize)) return false;

            float y = bodyTopLeft.Y + bodySize.Y;

            float outputHeight = OutputStackHeight(node);
            if (outputHeight > 0f) y += outputHeight + VgsConstants.PortStackSpacing;

            float inputHeight = InputStackHeight(node);
            if (inputHeight > 0f) y += inputHeight + VgsConstants.PortStackSpacing;

            y += watch.Slot * PortBarStride;

            topLeft = new Vector2(bodyTopLeft.X, y);
            size = new Vector2(bodySize.X, VgsConstants.PortBarHeight);
            return true;
        }

        /// <summary>
        /// Centre of the dot on a watch bar: inside the bar, at the right edge, the same size
        /// and the same four-pixel gap as every other port control.
        ///
        /// Right, not left, because the line <em>leaves</em> this port for the watched block.
        /// The side of the dot is what tells an input from an output at a glance, and a watch
        /// is an output of sorts: the controller is the one that acts, the watched block only
        /// reports its state.
        /// </summary>
        public static bool TryWatchDot(Node node, WatchTarget watch, float aspectX, out Vector2 center)
        {
            center = Vector2.Zero;

            Vector2 topLeft, size;
            if (!TryWatchBarRect(node, watch, out topLeft, out size)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            center = new Vector2(topLeft.X + size.X - ControlOffset(aspectX),
                                 topLeft.Y + size.Y * 0.5f);
            return true;
        }

        /// <summary>
        /// Centre of the "x" on a watch bar: the dot's mirror image, on the left. Clicking it
        /// removes the block from the controller's list (ADR-0017).
        /// </summary>
        public static bool TryWatchRemoveButton(Node node, WatchTarget watch, float aspectX,
                                                out Vector2 center)
        {
            center = Vector2.Zero;

            Vector2 topLeft, size;
            if (!TryWatchBarRect(node, watch, out topLeft, out size)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            center = new Vector2(topLeft.X + ControlOffset(aspectX),
                                 topLeft.Y + size.Y * 0.5f);
            return true;
        }

        /// <summary>
        /// Rectangle of a watch bar's label: the span between the two round controls, the same
        /// span an input or output bar gets. The label is the watched block's name.
        /// </summary>
        public static bool TryWatchLabelRect(Node node, WatchTarget watch, float aspectX,
                                             out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;

            Vector2 barTopLeft, barSize;
            if (!TryWatchBarRect(node, watch, out barTopLeft, out barSize)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float inset = ControlOffset(aspectX) + ControlHalfWidth(aspectX)
                          + VgsConstants.PortLabelPadding;
            float width = barSize.X - inset * 2f;
            if (width <= 0f) return false;

            topLeft = new Vector2(barTopLeft.X + inset, barTopLeft.Y);
            size = new Vector2(width, barSize.Y);
            return true;
        }

        /// <summary>The watch target whose "x" is under a graph-space point, or null.</summary>
        public static WatchTarget HitWatchRemoveButton(Node node, Vector2 graphPoint, float aspectX)
        {
            if (node == null || node.Controller == null) return null;
            if (aspectX <= 0f) aspectX = 1f;

            float tolerance = VgsConstants.PortControlRadius * VgsConstants.PortControlHitTolerance;

            List<WatchTarget> watched = node.Controller.Watched;
            for (int i = 0; i < watched.Count; i++)
            {
                WatchTarget watch = watched[i];
                if (watch.Slot < 0) continue;

                Vector2 center;
                if (!TryWatchRemoveButton(node, watch, aspectX, out center)) continue;

                float dx = (graphPoint.X - center.X) / aspectX;
                float dy = graphPoint.Y - center.Y;
                if (dx * dx + dy * dy <= tolerance * tolerance) return watch;
            }
            return null;
        }

        /// <summary>
        /// Centre of a node's card. A watch line ends here rather than on a port: the watched
        /// block has no "observe me" port, and a line into one of its real ports would claim a
        /// call the controller never makes (ADR-0017).
        ///
        /// Cards are drawn after lines, so the visible end of the line is the card's border —
        /// which is exactly "the line ends in the card".
        /// </summary>
        public static bool TryCardCentre(Node node, out Vector2 center)
        {
            center = Vector2.Zero;
            if (node == null) return false;

            Vector2 topLeft, size;
            if (!TryBodyRect(node, out topLeft, out size)) return false;

            center = topLeft + size * 0.5f;
            return true;
        }

        /// <summary>Number of watch bars of this node that are actually drawn.</summary>
        public static int DrawnWatchCount(Node node)
        {
            if (node == null || node.Controller == null) return 0;

            List<WatchTarget> watched = node.Controller.Watched;
            int count = 0;
            for (int i = 0; i < watched.Count; i++)
            {
                if (watched[i].Slot >= 0) count++;
            }
            return count;
        }

        /// <summary>Height of the watch stack under a card, for culling and hit-testing.</summary>
        public static float WatchStackHeight(Node node)
        {
            int count = DrawnWatchCount(node);
            if (count == 0) return 0f;
            return count * PortBarStride - VgsConstants.PortBarGap;
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

        /// <summary>Total height a node occupies: body plus all three port stacks.</summary>
        public static float TotalHeight(Node node)
        {
            Vector2 bodyTopLeft, bodySize;
            if (!TryBodyRect(node, out bodyTopLeft, out bodySize)) return 0f;

            float output = OutputStackHeight(node);
            float input = InputStackHeight(node);
            float watch = WatchStackHeight(node);

            float total = bodySize.Y + output;
            if (input > 0f)
            {
                if (output > 0f) total += VgsConstants.PortStackSpacing;
                total += input;
            }
            if (watch > 0f)
            {
                // The watch stack follows the same rule as the input stack: the separating gap
                // is added only when there is something above, or the first watch bar would
                // float away from the card it belongs to.
                if (output > 0f || input > 0f) total += VgsConstants.PortStackSpacing;
                total += watch;
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
                || HitAnyPortBar(node.Actions, node, graphPoint)
                || HitAnyWatchBar(node, graphPoint);
        }

        /// <summary>
        /// True when the point is on one of the node's drawn watch bars. A watch bar belongs to
        /// the controller for the same reason a port bar belongs to its block: a click there
        /// must select the node, not start a pan under it (ADR-0017).
        /// </summary>
        private static bool HitAnyWatchBar(Node node, Vector2 graphPoint)
        {
            if (node.Controller == null) return false;

            List<WatchTarget> watched = node.Controller.Watched;
            for (int i = 0; i < watched.Count; i++)
            {
                Vector2 topLeft, size;
                if (!TryWatchBarRect(node, watched[i], out topLeft, out size)) continue;
                if (Contains(topLeft, size, graphPoint)) return true;
            }
            return false;
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
