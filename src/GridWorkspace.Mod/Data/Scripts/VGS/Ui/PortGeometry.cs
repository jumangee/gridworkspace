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
    /// <b>The shape.</b> A card has three parts, and each has a name — because "left" and "right"
    /// say nothing about what is there, and stop being true the day a part moves
    /// (<c>docs/06-terminology.md</c> §4, «Три части карточки»):
    ///
    ///  * <b>the state column</b> (колонка состояния) — the narrow strip on the card's leading
    ///    edge, inset from the card's border on all four sides, split into an upper band (40% of
    ///    the inset height) and a lower one. It holds controls and readings about <em>the block
    ///    itself</em>: today the power switch in the upper band, and the lower band is reserved.
    ///    <see cref="TryPowerSwitchRect"/> owns it.
    ///  * <b>the nameplate</b> (шильдик) — the middle: the block's name, its subtype and, for a
    ///    controller, its mode description. It answers "what the block is".
    ///    <see cref="TryCardTextSpan"/> owns its horizontal span.
    ///  * <b>the button row</b> (ряд кнопок) — the row of gestures along the trailing edge: jump
    ///    to the terminal, watch, bind. It answers "what can be done from the card".
    ///    <see cref="TryCardButtonRow"/> owns it.
    ///
    /// The three questions — <i>what it is doing now / what it is / what can be done</i> — are the
    /// reason the names differ: they are the same distinction <see cref="BlockState"/> draws
    /// between a block's state and its identity, so a reader who has met one has met all three.
    /// The three parts do not mix: each has its own sizing rule, which is why adding the switch did
    /// not move the row by a pixel and only pushed the nameplate to the right (ADR-0035).
    ///
    /// Ports are rectangles as wide as the card, hanging underneath it, flush against each other.
    /// Each bar carries two round controls of the same size, one at each end:
    ///
    /// <code>
    ///   +------------------------------------+
    ///   | (o=) Event Controller  (>) (#) (T) (F) |  card: state column, nameplate, button row
    ///   |      EventControllerLarge              |
    ///   +------------------------------------+
    ///   | (x) Fuel Changed          (o) |   output bar: dot at the right (lines leave), "x" at the left
    ///   | (x) Out Of Fuel           (o) |
    ///   +------------------------------------+
    ///   | (o) On Off                (x) |   input bar: dot at the left (lines arrive), "x" at the right
    ///   +------------------------------------+
    /// </code>
    ///
    /// <b>All of the button row is in that row and nowhere else</b> (ADR-0020). Which buttons a card
    /// has follows from what the block is: every block gets the jump button (ADR-0021), a timer
    /// then gets one bind button, a sensor two (one per fire group), an event controller four (the
    /// bell and the two bind buttons after the jump). They share one size and one vertical centre,
    /// so two cards side by side look like two instances of the same control vocabulary instead of
    /// two different designs. The state column is deliberately <em>not</em> part of it: it holds a
    /// control that acts on the block, not a gesture on the schema, so it has its own size rule.
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
    /// <b>An input bar has its own slot, because it has its own request.</b> Since an action may
    /// take arguments, one action port can need several bars — one per distinct set of values —
    /// so the slot lives on <see cref="InputBar"/> and its geometry is
    /// <c>TryInputBarRect</c>/<c>TryInputBarDot</c>/… (ADR-0050). The methods here that take a
    /// <see cref="Port"/> therefore answer only for an output port, and return false for an
    /// input one: a bar drawn through them would be a bar with no arguments.
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
        /// Rectangle covering the whole <b>button row</b> of a node, in graph units. False when the
        /// card carries no buttons.
        ///
        /// It exists for the one consumer that has to know where the row *ends* rather than where
        /// a button is: the nameplate is clipped to the span left of it, so a long block name can
        /// neither run under a button nor spill over the card.
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
        /// True when this node's card carries a state column with the block-power switch in it
        /// (ADR-0035).
        ///
        /// The switch is a control on a *block*: it acts on the block's own on/off flag, so a
        /// group — which is not an entity and has no flag — has none, and a block the game does not
        /// report as functional has none either, because it has nothing to switch. That is a fact
        /// about the block's type, cached on the node when it was built
        /// (<see cref="Node.CanBeEnabled"/>), not about its power: the column is reserved for the
        /// block's whole life, so switching it off cannot move its own name sideways.
        ///
        /// This used to be the predicate that decided whether the card had a <b>state column</b> at
        /// all. Since ADR-0042 it is not: a cargo container carries a state widget and no switch, so
        /// the column exists without one. <see cref="HasStateColumn"/> answers that question now, and
        /// this one answers "is there something to click on the leading edge".
        /// </summary>
        public static bool HasPowerSwitch(Node node)
        {
            return node != null && !node.IsGroup && node.CanBeEnabled;
        }

        /// <summary>
        /// Inner height of a node's <b>state column</b>, in the layout's vertical units: the card's
        /// height less the vertical padding above and below (ADR-0035).
        ///
        /// This is the box the column's bands are measured against. It exists as a method rather
        /// than as arithmetic inside <see cref="PowerSwitchInkHeight"/> because two consumers need
        /// it — the switch's rectangle and the offline gate's assertion that the column leaves the
        /// same gap at both ends — and because "the column's inner box" is the thing the padding
        /// actually defines. A second subtraction elsewhere is how one of the two ends ends up
        /// padded and the other not.
        ///
        /// Returns 0 for a null node, and clamps at 0 for a card too short to hold the padding at
        /// all: a negative height would place the switch above its own card.
        /// </summary>
        public static float PowerSwitchContentHeight(Node node)
        {
            if (node == null) return 0f;

            float inner = CardHeight(node) - 2f * VgsConstants.PowerSwitchPadding;
            return inner > 0f ? inner : 0f;
        }

        /// <summary>
        /// Height of the power switch's ink, in the layout's vertical units: the upper band of the
        /// card's state column, which the icon fills exactly.
        ///
        /// The band is a fraction of the column's *inner* height (<see cref="PowerSwitchContentHeight"/>),
        /// not of the card's: the column keeps a two-pixel gap from the card's top and bottom edges,
        /// and the two bands split what is left between them.
        /// </summary>
        public static float PowerSwitchInkHeight(Node node)
        {
            if (node == null) return 0f;
            return PowerSwitchContentHeight(node) * VgsConstants.PowerSwitchBandRatio;
        }

        /// <summary>
        /// Width of the power switch's ink, in the layout's vertical units.
        ///
        /// <b>It is derived from the icon, not chosen.</b> The switch fills its slot by height, and
        /// the icon is wider than it is tall — the atlas build measures the ink at 576 × 384, an
        /// aspect of 1.5 — so the width is 1.5 × the slot. That is exactly 60% of the card's height,
        /// the same figure the row's buttons use for their diameter, which is what makes a switch
        /// and a button read as one size on one card (ADR-0035).
        ///
        /// The value is in *vertical* units on purpose, like every other width in this type: the
        /// conversion to layout X units goes through the client aspect at the call site, so the
        /// switch is as wide on screen as the icon really is. Multiplying by the aspect here would
        /// make the number depend on the window's shape and lose its meaning.
        /// </summary>
        public static float PowerSwitchInkWidth(Node node)
        {
            return PowerSwitchInkHeight(node) * PowerSwitchInkAspect;
        }

        /// <summary>
        /// Width ÷ height of the switch icon's ink, taken from the generated atlas table.
        ///
        /// It is read from the table rather than written down, because it is a property of the
        /// texture: a redrawn icon would change the number, and a copy here would keep reserving
        /// the old column and let the ink run into the block's name. The offline gate asserts that
        /// both switch icons have the same aspect, so the column cannot change width between the
        /// two states of one block.
        /// </summary>
        public static float PowerSwitchInkAspect
        {
            get { return VgsIconAtlas.InkAspect(VgsIcon.ToggleOn); }
        }

        /// <summary>
        /// Rectangle of the power switch's ink on a node's card, in graph units, or false when the
        /// node has no switch. It is the upper band of the card's <b>state column</b>.
        ///
        /// The rectangle is the *ink*, not the sprite's destination: the texture is square and the
        /// pill inside it is wider than tall, so a caller that reserved the sprite's box would
        /// reserve twice the room it needs. Both the draw pass and the hit test start here, which
        /// is why the ink box is what this returns — the two cannot disagree about how big the
        /// control is.
        ///
        /// <b>The switch is inset from every card edge it faces.</b> Horizontally it keeps
        /// <see cref="VgsConstants.CardButtonMargin"/> from the leading border — "close to the left
        /// edge", as the requirement puts it, and the same margin the button row keeps from the
        /// trailing edge. Vertically it keeps <see cref="VgsConstants.PowerSwitchPadding"/> from the
        /// card's top edge, and the lower band keeps the same gap from the bottom one: a control
        /// whose ink lies *on* the frame reads as part of the frame rather than as a control on the
        /// card.
        /// </summary>
        public static bool TryPowerSwitchRect(Node node, float aspectX, out Vector2 topLeft,
                                              out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;
            if (!HasPowerSwitch(node)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float inkHeight = PowerSwitchInkHeight(node);
            if (inkHeight <= 0f) return false;

            float inkWidth = inkHeight * PowerSwitchInkAspect * aspectX;

            // The corner comes from the column, not from a second derivation of "card plus margin
            // plus padding": the switch and the widget under it have to sit in one strip, and the
            // strip is what TryStateColumnRect describes (ADR-0042).
            Vector2 columnTopLeft, columnSize;
            if (!TryStateColumnRect(node, aspectX, out columnTopLeft, out columnSize)) return false;

            topLeft = new Vector2(columnTopLeft.X, columnTopLeft.Y);
            size = new Vector2(inkWidth, inkHeight);
            return true;
        }

        /// <summary>
        /// True when a graph-space point is on a node's power switch.
        ///
        /// The target is the drawn ink plus <see cref="VgsConstants.PowerSwitchHitPadding"/> on
        /// every side, and the padded rectangle is <b>clipped to the card</b>: the switch sits on
        /// the card's top-left corner, so an unclipped tolerance would make a click just above the
        /// card toggle a block the player did not point at. A control is allowed to be forgiving
        /// inside its own shape, not outside it.
        ///
        /// <b>Only the vertical clamp is load-bearing today, and that is stated rather than
        /// implied.</b> The padding and the switch's own margin from the border are the same four
        /// pixels, so the padded box can never reach past the card's left edge: the two constants
        /// cancel. The horizontal clamps are kept because the rule must not depend on two
        /// constants staying equal — raising the padding or lowering the margin would otherwise
        /// silently push the target outside the card — but no test can tell them apart while the
        /// constants match, and
        /// <c>offline-check</c> asserts the relationship instead of pretending to test the clamp.
        /// </summary>
        public static bool HitPowerSwitch(Node node, Vector2 graphPoint, float aspectX)
        {
            Vector2 topLeft, size;
            if (!TryPowerSwitchRect(node, aspectX, out topLeft, out size)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            Vector2 cardTopLeft, cardSize;
            CardRect(node, out cardTopLeft, out cardSize);

            float padY = VgsConstants.PowerSwitchHitPadding;
            float padX = padY * aspectX;

            float left = topLeft.X - padX;
            float top = topLeft.Y - padY;
            float right = topLeft.X + size.X + padX;
            float bottom = topLeft.Y + size.Y + padY;

            if (left < cardTopLeft.X) left = cardTopLeft.X;
            if (top < cardTopLeft.Y) top = cardTopLeft.Y;
            if (right > cardTopLeft.X + cardSize.X) right = cardTopLeft.X + cardSize.X;
            if (bottom > cardTopLeft.Y + cardSize.Y) bottom = cardTopLeft.Y + cardSize.Y;

            if (right <= left || bottom <= top) return false;

            return Contains(new Vector2(left, top), new Vector2(right - left, bottom - top), graphPoint);
        }

        /// <summary>
        /// True when this node's card carries a <b>state widget</b> in the lower band of its state
        /// column (ADR-0042).
        ///
        /// <b>It is a fact about the block, not about its state.</b> A cargo container has a fill
        /// level for its whole life; whether the card draws it right now is a different question
        /// (<see cref="HasStateColumn"/> keeps the column's width either way, so a widget that stops
        /// being drawn cannot move the block's name). The fact is <see cref="Node.Widget"/>, which
        /// the editor read from the game's interfaces rather than from the subtype (ADR-0042 §7).
        /// A group has no reading of its own, which is why the group check is here and not at the
        /// call sites.
        /// </summary>
        public static bool HasStateWidget(Node node)
        {
            return node != null && !node.IsGroup && node.Widget != StateWidgetKind.None;
        }

        /// <summary>
        /// True when the card has a <b>state column</b> at all: a switch, a widget, or both
        /// (ADR-0035, ADR-0042).
        ///
        /// <b>This is the predicate the nameplate is indented by.</b> Until ADR-0042 the column
        /// existed exactly where the switch did, so one predicate answered both questions. A cargo
        /// container has a widget and no switch, and it still needs the column — so the two
        /// questions are now separate, and this is the one that says "there is something on the
        /// leading edge".
        /// </summary>
        public static bool HasStateColumn(Node node)
        {
            return HasPowerSwitch(node) || HasStateWidget(node);
        }

        /// <summary>
        /// Width of a node's <b>state column</b>, in the layout's vertical units — so a caller turns
        /// it into a horizontal length by multiplying by the client aspect, like every other width
        /// in this type.
        ///
        /// <b>The widget's band decides the width when there is no switch, and the switch's icon
        /// decides it when there is one.</b> The column is 0.6 of its inner height wide, because the
        /// widget's band is 0.6 of that height — one set of proportions for every card. For a block
        /// that also has a switch, the icon's own width wins if it is larger — a redrawn toggle
        /// would otherwise be clipped by a column sized for the widget.
        ///
        /// The two are exactly equal today: the switch is 1.5 × its 40% band, and 1.5 × 0.4 = 0.6.
        /// The <c>max</c> is what keeps that from being a coincidence the layout depends on. The band
        /// also loses <see cref="VgsConstants.WidgetTopGap"/> in height, so the area is no longer a
        /// square (ADR-0042 §3a); the width still comes from the band's full 0.6.
        /// </summary>
        public static float StateColumnWidth(Node node)
        {
            if (!HasStateColumn(node)) return 0f;

            float inner = PowerSwitchContentHeight(node);
            if (inner <= 0f) return 0f;

            float width = inner * (1f - VgsConstants.PowerSwitchBandRatio);

            if (HasPowerSwitch(node))
            {
                float ink = PowerSwitchInkWidth(node);
                if (ink > width) width = ink;
            }

            return width;
        }

        /// <summary>
        /// Rectangle of a node's whole <b>state column</b>, in graph units: the card's leading edge,
        /// inset from all four sides (ADR-0035 §1).
        ///
        /// It exists as one method so the switch, the widget and the nameplate's left end cannot
        /// disagree about where the column is. Before ADR-0042 each of them re-derived its own part
        /// of this rectangle; a cargo container — which has a widget and no switch — is what showed
        /// that "the column starts at the switch" stops being true.
        /// </summary>
        public static bool TryStateColumnRect(Node node, float aspectX, out Vector2 topLeft,
                                              out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;
            if (!HasStateColumn(node)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float inner = PowerSwitchContentHeight(node);
            float width = StateColumnWidth(node);
            if (inner <= 0f || width <= 0f) return false;

            Vector2 cardTopLeft, cardSize;
            CardRect(node, out cardTopLeft, out cardSize);

            topLeft = new Vector2(cardTopLeft.X + VgsConstants.CardButtonMargin * aspectX,
                                  cardTopLeft.Y + VgsConstants.PowerSwitchPadding);
            size = new Vector2(width * aspectX, inner);
            return true;
        }

        /// <summary>
        /// Rectangle of a node's <b>state widget area</b> in graph units, or false when the card has
        /// no widget. It is the lower band of the state column, less the gap above it.
        ///
        /// <b>As wide as the column, and shorter than that by one gap.</b> The column is 0,6 of its
        /// inner height wide and the lower band is the other 0,6 of it — so the band alone would be a
        /// square — but <see cref="VgsConstants.WidgetTopGap"/> is taken off the top, because the
        /// switch and the widget must not touch (ADR-0042 §3a). The area is therefore slightly wider
        /// than it is tall, and every shape is inscribed in it rather than stretched to fill it: the
        /// clock's face takes the shorter side, the bars take the height they are given.
        ///
        /// <b>It is the area, not the drawing.</b> What is drawn inside it — the frame's padding, the
        /// fill's inset, the bolt's column — is decided by the shape, and each of those numbers is
        /// relative to this rectangle rather than to the card.
        /// </summary>
        public static bool TryStateWidgetArea(Node node, float aspectX, out Vector2 topLeft,
                                              out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;
            if (!HasStateWidget(node)) return false;

            Vector2 columnTopLeft, columnSize;
            if (!TryStateColumnRect(node, aspectX, out columnTopLeft, out columnSize)) return false;

            float band = columnSize.Y * (1f - VgsConstants.PowerSwitchBandRatio)
                         - VgsConstants.WidgetTopGap;
            if (band <= 0f) return false;

            topLeft = new Vector2(columnTopLeft.X, columnTopLeft.Y + columnSize.Y - band);
            size = new Vector2(columnSize.X, band);
            return true;
        }

        /// <summary>
        /// Padding a widget's frame keeps inside its area, as a share of the area on each axis,
        /// <b>both sides together</b> (ADR-0042 §3).
        ///
        /// The plain fill bar gets the two numbers the requirement named for it — 15% of the width,
        /// 5% of the height — and the shapes that are already split inside (a battery's bolt column,
        /// a production block's two bars) get a smaller one, because 15% of a half-width is three
        /// pixels of fill. The clock gets the smallest: a circle in a square leaves its corners
        /// empty anyway, and padding it like a bar would shorten the hands, which are the reading.
        /// </summary>
        public static void StateWidgetPadding(StateWidgetKind kind, out float padX, out float padY)
        {
            switch (kind)
            {
                case StateWidgetKind.FillBar:
                    padX = VgsConstants.WidgetPaddingFillWidth;
                    padY = VgsConstants.WidgetPaddingFillHeight;
                    return;

                case StateWidgetKind.Clock:
                    padX = VgsConstants.WidgetPaddingClock;
                    padY = VgsConstants.WidgetPaddingClock;
                    return;

                default:
                    padX = VgsConstants.WidgetPaddingWidth;
                    padY = VgsConstants.WidgetPaddingHeight;
                    return;
            }
        }

        /// <summary>
        /// One bar of a node's state widget, in graph units: its frame (the outer rectangle, drawn as
        /// a border) and its fill (the inner one, filled from the bottom up to the reading).
        ///
        /// <b>The fill is the reading, and it grows upwards.</b> An empty container is an empty
        /// rectangle — the frame alone — and a full one is a rectangle filled to its own top edge,
        /// <see cref="VgsConstants.WidgetInsetPixels"/> short of the frame on every side. The inset is
        /// what makes the two rectangles readable as two: without it a full bar and a solid block
        /// would be the same picture.
        ///
        /// <b>Bar index is metric index.</b> The first bar reads metric 0 and the second reads metric
        /// 1, so a caller draws bar <c>i</c> without knowing whether it is looking at a container's
        /// fill, a battery's charge or a production block's output — the descriptor's key order is
        /// the only place that knows.
        ///
        /// Returns false for a bar the shape does not have (index out of range, no widget), and
        /// returns true with a zero-height fill for a reading of zero: an empty bar is drawn, because
        /// the frame is the part that says "there is a reading here and it is empty".
        ///
        /// <b>A true result with a degenerate frame leaves <paramref name="fillTopLeft"/> at the
        /// origin.</b> That happens when the card is too small for the inset (a zoomed-out card
        /// still draws its frame, but there is no room inside it), and the contract is that the fill
        /// is meaningful only while <paramref name="fillSize"/> is positive — which is what the draw
        /// path tests before painting. Returning false instead would drop the frame as well, and an
        /// empty container would lose the rectangle that says it is empty.
        /// </summary>
        public static bool TryStateWidgetBar(Node node, float aspectX, int bar,
                                             out Vector2 frameTopLeft, out Vector2 frameSize,
                                             out Vector2 fillTopLeft, out Vector2 fillSize)
        {
            frameTopLeft = Vector2.Zero;
            frameSize = Vector2.Zero;
            fillTopLeft = Vector2.Zero;
            fillSize = Vector2.Zero;

            if (!HasStateWidget(node)) return false;

            int count = StateWidget.BarCount(node.Widget);
            if (bar < 0 || bar >= count) return false;

            Vector2 areaTopLeft, areaSize;
            if (!TryStateWidgetArea(node, aspectX, out areaTopLeft, out areaSize)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float padX, padY;
            StateWidgetPadding(node.Widget, out padX, out padY);

            float left = areaTopLeft.X + areaSize.X * padX * 0.5f;
            float top = areaTopLeft.Y + areaSize.Y * padY * 0.5f;
            float width = areaSize.X * (1f - padX);
            float height = areaSize.Y * (1f - padY);

            // A battery gives the trailing part of its padded width to the bolt, so the bar is what
            // is left: the bolt sits beside the bar, never on top of it (ADR-0042 §4).
            //
            // The column is reserved even in `Auto`, when no bolt is drawn. A bar that grew when the
            // mode changed would make one charge reading look like two, and the mode is a fact about
            // the block, not about its charge — so the shape keeps the room and the accent only fills
            // it or does not.
            if (node.Widget == StateWidgetKind.Battery)
            {
                width = width - width * VgsConstants.WidgetBoltShare
                        - VgsConstants.WidgetBoltGap * aspectX;
            }

            float gap = count > 1 ? VgsConstants.WidgetBarGap * aspectX : 0f;
            float barWidth = (width - gap * (count - 1)) / count;
            if (barWidth <= 0f || height <= 0f) return false;

            frameTopLeft = new Vector2(left + bar * (barWidth + gap), top);
            frameSize = new Vector2(barWidth, height);

            float insetX = VgsConstants.WidgetInset * aspectX;
            float insetY = VgsConstants.WidgetInset;

            float innerWidth = frameSize.X - 2f * insetX;
            float innerHeight = frameSize.Y - 2f * insetY;
            if (innerWidth <= 0f || innerHeight <= 0f) return true;

            // Re-clamped even though BlockState.SetMetric already does: the array is public, so the
            // write-side clamp is a guarantee about writes, not about reads (ADR-0042 §6). A bar
            // drawn past the top of its own frame is the one defect this indicator cannot afford.
            float value = node.State != null ? node.State.GetMetric(bar) : 0f;
            if (value < 0f) value = 0f;
            else if (value > 1f) value = 1f;

            float filled = innerHeight * value;

            fillSize = new Vector2(innerWidth, filled);
            fillTopLeft = new Vector2(frameTopLeft.X + insetX,
                                      frameTopLeft.Y + frameSize.Y - insetY - filled);
            return true;
        }

        /// <summary>
        /// Centre and radius of a node's <b>clock</b> face, in graph units, or false when the card
        /// has no clock. The radius is in the layout's vertical units; a caller turns it into a
        /// horizontal one by multiplying by the client aspect, exactly as <c>VgsScreenDraw.Disc</c>
        /// does, so the face is round on screen.
        ///
        /// <b>The face is inscribed in the padded area, not stretched to it.</b> The area is a
        /// rectangle in graph units whose axes have different scales — and since ADR-0042 §3a it is
        /// not even a square on screen, being one gap wider than it is tall — so the diameter is the
        /// smaller of the two sides measured in vertical units. Stretching it would draw an ellipse
        /// the moment a redrawn toggle changed the column's width.
        /// </summary>
        public static bool TryStateWidgetClock(Node node, float aspectX, out Vector2 center,
                                               out float radius)
        {
            center = Vector2.Zero;
            radius = 0f;

            if (!HasStateWidget(node) || node.Widget != StateWidgetKind.Clock) return false;

            Vector2 areaTopLeft, areaSize;
            if (!TryStateWidgetArea(node, aspectX, out areaTopLeft, out areaSize)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float padX, padY;
            StateWidgetPadding(node.Widget, out padX, out padY);

            float width = areaSize.X * (1f - padX);
            float height = areaSize.Y * (1f - padY);

            // Both sides in vertical units: the width has to lose the aspect to be comparable.
            float diameter = width / aspectX;
            if (height < diameter) diameter = height;

            radius = diameter * 0.5f;
            center = new Vector2(areaTopLeft.X + areaSize.X * 0.5f,
                                 areaTopLeft.Y + areaSize.Y * 0.5f);
            return radius > 0f;
        }

        /// <summary>
        /// Centre and ink height of a battery's charge-mode <b>bolt</b>, in graph units, or false
        /// when the card has no battery widget.
        ///
        /// <b>The bolt is sized by its column, not by the area.</b> It sits in the share of the width
        /// that <see cref="TryStateWidgetBar"/> left free, and it fills that share across: the ink's
        /// height is the column's width divided by the icon's own aspect, which the atlas build
        /// measures. A redrawn bolt therefore changes its own size and nothing else — the same rule
        /// the power switch follows with <see cref="PowerSwitchInkAspect"/>.
        ///
        /// <b>It is vertically centred, not aligned to the fill.</b> The bolt says which way the
        /// charge is going, not how much of it there is; aligning it with the top of the fill would
        /// make it move on every reading, which is motion that means nothing.
        /// </summary>
        public static bool TryStateWidgetBolt(Node node, float aspectX, out Vector2 center,
                                              out float inkHeight)
        {
            center = Vector2.Zero;
            inkHeight = 0f;

            if (!HasStateWidget(node) || node.Widget != StateWidgetKind.Battery) return false;

            Vector2 areaTopLeft, areaSize;
            if (!TryStateWidgetArea(node, aspectX, out areaTopLeft, out areaSize)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float padX, padY;
            StateWidgetPadding(node.Widget, out padX, out padY);

            float width = areaSize.X * (1f - padX);
            float height = areaSize.Y * (1f - padY);

            float column = width * VgsConstants.WidgetBoltShare;
            if (column <= 0f) return false;

            // `column` is a width in the layout's *horizontal* units, and the ink's height is a
            // vertical length: the conversion goes through the client aspect, exactly as the
            // radius does in TryStateWidgetClock. Dropping it would make the bolt `aspect` times
            // too short — 0.56× on a 16:9 window, which reads as a dash rather than a bolt.
            float inkWidth = column;
            inkHeight = inkWidth / (aspectX * BoltInkAspect);
            if (inkHeight <= 0f) return false;

            // The column's centre is the padded area's trailing end, minus half of what it takes.
            center = new Vector2(areaTopLeft.X + areaSize.X * (1f - padX * 0.5f) - column * 0.5f,
                                 areaTopLeft.Y + areaSize.Y * padY * 0.5f + height * 0.5f);
            return true;
        }

        /// <summary>
        /// Width ÷ height of the charge-mode bolt's ink, taken from the generated atlas table — the
        /// same rule as <see cref="PowerSwitchInkAspect"/>: the number belongs to the texture, and a
        /// copy here would size the bolt against an icon nobody draws any more.
        /// </summary>
        public static float BoltInkAspect
        {
            get { return VgsIconAtlas.InkAspect(VgsIcon.Bolt); }
        }

        /// <summary>
        /// Where a clock's hand ends, given the fraction of the countdown that has elapsed.
        ///
        /// <b>Zero is twelve o'clock and the hand sweeps clockwise</b> (ADR-0042 §4): the fixed hand
        /// marks where the timer will be when it fires, so the moving one has to arrive there rather
        /// than leave from there. Screen Y grows downwards, so "up" is a negative sine — the sign that
        /// a hand at zero points at the top of the face rather than the bottom.
        ///
        /// <b>The angle is computed in screen proportions, the result in graph units.</b> One X unit
        /// of the layout is the window's width and one Y unit its height, so a circle is an ellipse
        /// in graph units; the horizontal component is multiplied by the aspect, exactly as the
        /// radius is in <see cref="TryStateWidgetClock"/>. Doing it the other way round — an angle
        /// taken from graph-space extents — would make the hand sweep an ellipse.
        /// </summary>
        public static Vector2 StateWidgetHandTip(Vector2 center, float radius, float aspectX,
                                                 float ratio, float turns)
        {
            if (aspectX <= 0f) aspectX = 1f;

            if (turns < 0f) turns = 0f;
            else if (turns > 1f) turns = 1f;

            // Twelve o'clock is -90 degrees, and a full turn is 2π; clockwise on screen is a
            // positive angle because Y grows downwards.
            double angle = -Math.PI * 0.5 + turns * Math.PI * 2.0;
            float reach = radius * ratio;

            return new Vector2(center.X + (float)Math.Cos(angle) * reach * aspectX,
                               center.Y + (float)Math.Sin(angle) * reach);
        }

        /// <summary>
        /// Horizontal span of the card's <b>nameplate</b>, in graph units: from the card's inner
        /// padding (or from the state column, when there is one) to the leading edge of the button
        /// row, less one gap.
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
        ///
        /// <b>The left end is the state column when there is one</b> (ADR-0035). The nameplate
        /// follows the column, because the name is what the player reads first and it must not run
        /// under a control. That is a second consumer of the same rectangle the switch is drawn in
        /// — which is why it is asked for here rather than recomputed: a span derived separately is
        /// how a label ends up half a control away from where it should be.
        /// </summary>
        public static bool TryCardTextSpan(Node node, float aspectX, out float left, out float right)
        {
            left = 0f;
            right = 0f;

            Vector2 rowTopLeft, rowSize;
            if (!TryCardButtonRow(node, aspectX, out rowTopLeft, out rowSize)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            left = node.Position.X + VgsConstants.NodeWidth * CardTextPaddingRatio;

            // The column, whatever it holds — a switch, a widget, or both (ADR-0042). Asking for
            // the switch's rectangle here was the same thing until a cargo container arrived with a
            // widget and no switch: that card would have drawn its name under its own widget.
            Vector2 columnTopLeft, columnSize;
            if (TryStateColumnRect(node, aspectX, out columnTopLeft, out columnSize))
            {
                left = columnTopLeft.X + columnSize.X + VgsConstants.CardButtonTextGap * aspectX;
            }

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

        /// <summary>
        /// True when a watch target's dashed line is drawn at all (ADR-0017, ADR-0036).
        ///
        /// <b>A controller watching itself has a bar but no line.</b> The line would start at the
        /// controller's own watch bar and end in its own card, and cards are drawn after lines —
        /// so the whole route except a stub beside the card would be hidden behind that card. The
        /// bar is the picture of this connection: it carries the controller's own name, which no
        /// other bar on that card can.
        ///
        /// It lives here rather than in the draw loop because it is a rule about *what is
        /// drawn*, and the draw pass may not be the only place that knows it: the offline gate
        /// asserts it, and the hit test walks the same bars.
        /// </summary>
        public static bool ShouldDrawWatchLine(Node controller, WatchTarget watch)
        {
            if (controller == null || watch == null) return false;
            return watch.EntityId != controller.EntityId;
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
        /// Rectangle of the bar for one <b>event</b> port, in graph units. Returns false for a
        /// port that is not drawn (no connection, or no free slot).
        ///
        /// <b>An input port has no bar of its own.</b> Since an action can be asked with
        /// arguments, one action port may need several bars — one per distinct request — and a
        /// single slot on the port cannot describe that. Input bars are
        /// <see cref="InputBar"/>s and their geometry is <see cref="TryInputBarRect"/>
        /// (ADR-0050). This method therefore answers false for every action port, which is also
        /// what keeps the old call sites honest: an input bar drawn through here would be a bar
        /// without arguments.
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
            if (port.Direction != PortDirection.Output) return false;

            Vector2 bodyTopLeft, bodySize;
            if (!TryBodyRect(node, out bodyTopLeft, out bodySize)) return false;

            float y = bodyTopLeft.Y + bodySize.Y + port.Slot * PortBarStride;

            topLeft = new Vector2(bodyTopLeft.X, y);
            size = new Vector2(bodySize.X, VgsConstants.PortBarHeight);
            return true;
        }

        /// <summary>
        /// Rectangle of one <b>input</b> bar, in graph units: an action asked with one set of
        /// arguments (ADR-0050). Returns false for a bar the layout did not place (slot below
        /// zero — the stack is full, or no connection carries that request).
        ///
        /// The input stack starts under the whole output stack, with the same separating gap the
        /// port bars have always had: the gap is added only when there *is* a stack above, or the
        /// first input bar would float away from the card it belongs to.
        /// </summary>
        public static bool TryInputBarRect(Node node, InputBar bar, out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;

            if (node == null || bar == null || bar.Slot < 0) return false;

            Vector2 bodyTopLeft, bodySize;
            if (!TryBodyRect(node, out bodyTopLeft, out bodySize)) return false;

            float y = bodyTopLeft.Y + bodySize.Y;
            float outputHeight = OutputStackHeight(node);
            if (outputHeight > 0f) y += outputHeight + VgsConstants.PortStackSpacing;

            y += bar.Slot * PortBarStride;

            topLeft = new Vector2(bodyTopLeft.X, y);
            size = new Vector2(bodySize.X, VgsConstants.PortBarHeight);
            return true;
        }

        /// <summary>
        /// Centre of the connection dot on an event port bar: inside the bar, one control-width
        /// and one four-pixel gap clear of the edge the line uses — the right edge.
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

            center = new Vector2(topLeft.X + size.X - offset, y);
            return true;
        }

        /// <summary>
        /// Centre of the connection dot on an input bar: the mirror image of an event port's —
        /// inside the bar, clear of its <b>left</b> edge, because that is where a line arrives.
        /// </summary>
        public static bool TryInputBarDot(Node node, InputBar bar, float aspectX, out Vector2 center)
        {
            center = Vector2.Zero;

            Vector2 topLeft, size;
            if (!TryInputBarRect(node, bar, out topLeft, out size)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            center = new Vector2(topLeft.X + ControlOffset(aspectX), topLeft.Y + size.Y * 0.5f);
            return true;
        }

        /// <summary>
        /// Centre of the remove button on an event port bar: the connection dot's mirror image,
        /// same size, on the opposite side of the bar.
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

            center = new Vector2(topLeft.X + ControlOffset(aspectX), topLeft.Y + size.Y * 0.5f);
            return true;
        }

        /// <summary>
        /// Centre of the remove button on an input bar: the right end, the dot's mirror image.
        /// </summary>
        public static bool TryInputBarRemoveButton(Node node, InputBar bar, float aspectX,
                                                   out Vector2 center)
        {
            center = Vector2.Zero;

            Vector2 topLeft, size;
            if (!TryInputBarRect(node, bar, out topLeft, out size)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            center = new Vector2(topLeft.X + size.X - ControlOffset(aspectX),
                                 topLeft.Y + size.Y * 0.5f);
            return true;
        }

        /// <summary>
        /// Centre of the <b>edit</b> button of an input bar — the pen that opens the argument
        /// dialog — or false when the bar has no arguments and therefore nothing to edit.
        ///
        /// <b>Only a parameterised bar has it.</b> A bar whose action takes no arguments has no
        /// values to change, and a control that opens an empty dialog is worse than no control:
        /// the player learns that the pen does nothing. That is the "if it is technically
        /// impossible, do not draw it" half of the requirement, applied to the case where it *is*
        /// possible but pointless (ADR-0050).
        ///
        /// It sits one control-pitch left of the remove button — the same size, the same gap, one
        /// row, so the two read as the bar's control pair rather than as two unrelated marks.
        /// </summary>
        public static bool TryInputBarEditButton(Node node, InputBar bar, float aspectX,
                                                 out Vector2 center)
        {
            center = Vector2.Zero;
            if (bar == null || !bar.CanEdit) return false;

            Vector2 removeCentre;
            if (!TryInputBarRemoveButton(node, bar, aspectX, out removeCentre)) return false;

            if (aspectX <= 0f) aspectX = 1f;

            float pitch = ControlPitch(aspectX);
            center = new Vector2(removeCentre.X - pitch, removeCentre.Y);
            return true;
        }

        /// <summary>
        /// The port whose remove button is under a graph-space point, or null. <b>Events only:</b>
        /// an input bar's "x" belongs to one request, not to the action, so it is found by
        /// <see cref="HitInputBarRemoveButton"/>.
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

            float tolerance = VgsConstants.PortControlRadius * VgsConstants.PortControlHitTolerance;

            for (int i = 0; i < node.Events.Count; i++)
            {
                Port port = node.Events[i];
                if (port.Slot < 0) continue;

                Vector2 center;
                if (!TryPortRemoveButton(node, port, aspectX, out center)) continue;
                if (IsOnControl(graphPoint, center, tolerance, aspectX)) return port;
            }
            return null;
        }

        /// <summary>
        /// The input bar whose remove button is under a graph-space point, or null.
        ///
        /// <b>The bar, not the port, and that is the whole point of arguments.</b> Two requests of
        /// one action are two bars; pressing "x" on one of them must delete that request and
        /// leave the other, or the player would lose a connection they never pointed at.
        /// </summary>
        public static InputBar HitInputBarRemoveButton(Node node, Vector2 graphPoint, float aspectX)
        {
            if (node == null) return null;
            if (aspectX <= 0f) aspectX = 1f;

            float tolerance = VgsConstants.PortControlRadius * VgsConstants.PortControlHitTolerance;

            for (int i = 0; i < node.InputBars.Count; i++)
            {
                InputBar bar = node.InputBars[i];
                if (bar.Slot < 0) continue;

                Vector2 center;
                if (!TryInputBarRemoveButton(node, bar, aspectX, out center)) continue;
                if (IsOnControl(graphPoint, center, tolerance, aspectX)) return bar;
            }
            return null;
        }

        /// <summary>
        /// The input bar whose edit button is under a graph-space point, or null. A bar with no
        /// arguments has no button, so it can never be returned here.
        /// </summary>
        public static InputBar HitInputBarEditButton(Node node, Vector2 graphPoint, float aspectX)
        {
            if (node == null) return null;
            if (aspectX <= 0f) aspectX = 1f;

            float tolerance = VgsConstants.PortControlRadius * VgsConstants.PortControlHitTolerance;

            for (int i = 0; i < node.InputBars.Count; i++)
            {
                InputBar bar = node.InputBars[i];
                if (bar.Slot < 0 || !bar.CanEdit) continue;

                Vector2 center;
                if (!TryInputBarEditButton(node, bar, aspectX, out center)) continue;
                if (IsOnControl(graphPoint, center, tolerance, aspectX)) return bar;
            }
            return null;
        }

        /// <summary>
        /// True when a point is on a round control centred at <paramref name="center"/>.
        ///
        /// The x distance is divided by the client aspect before it is compared with the radius:
        /// in layout units the disc is an ellipse, so a plain Euclidean distance would make it
        /// easy to hit from above and hard from the side — the same correction every round
        /// control in this file applies.
        /// </summary>
        private static bool IsOnControl(Vector2 graphPoint, Vector2 center, float tolerance,
                                        float aspectX)
        {
            float dx = (graphPoint.X - center.X) / aspectX;
            float dy = graphPoint.Y - center.Y;
            return dx * dx + dy * dy <= tolerance * tolerance;
        }

        /// <summary>
        /// Rectangle of an event port's label, in graph units: the span between the bar's two
        /// round controls.
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

            return LabelSpan(barTopLeft, barSize, 1, aspectX, out topLeft, out size);
        }

        /// <summary>
        /// Rectangle of an input bar's label: the action's name and its arguments, between the
        /// dot and whichever controls sit on the right end.
        ///
        /// <b>One control more on a parameterised bar</b> — the pen — so the label loses exactly
        /// one pitch. Sharing the arithmetic with the event label (through
        /// <see cref="LabelSpan"/>) is what keeps the two bars' text starting at the same place
        /// instead of drifting apart as the aspect changes.
        /// </summary>
        public static bool TryInputBarLabelRect(Node node, InputBar bar, float aspectX,
                                                out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;

            Vector2 barTopLeft, barSize;
            if (!TryInputBarRect(node, bar, out barTopLeft, out barSize)) return false;

            int rightControls = bar.CanEdit ? 2 : 1;
            return LabelSpan(barTopLeft, barSize, rightControls, aspectX, out topLeft, out size);
        }

        /// <summary>
        /// The label's span inside a bar: one control's width plus the gap on the left (the
        /// dot), and <paramref name="rightControls"/> pitches plus the gap on the right. False
        /// when the controls leave no room.
        /// </summary>
        private static bool LabelSpan(Vector2 barTopLeft, Vector2 barSize, int rightControls,
                                      float aspectX, out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;

            if (aspectX <= 0f) aspectX = 1f;
            if (rightControls < 1) rightControls = 1;

            float left = ControlOffset(aspectX) + ControlHalfWidth(aspectX) + VgsConstants.PortLabelPadding;

            // The right end holds `rightControls` controls of one pitch each; the leftmost of
            // them is the one the text must clear.
            float right = ControlOffset(aspectX) + ControlHalfWidth(aspectX)
                          + VgsConstants.PortLabelPadding
                          + (rightControls - 1) * ControlPitch(aspectX);

            float width = barSize.X - left - right;
            if (width <= 0f) return false;

            topLeft = new Vector2(barTopLeft.X + left, barTopLeft.Y);
            size = new Vector2(width, barSize.Y);
            return true;
        }

        /// <summary>Horizontal half-extent of a port control, in layout X units.</summary>
        private static float ControlHalfWidth(float aspectX)
        {
            return VgsConstants.PortControlRadius * aspectX;
        }

        /// <summary>
        /// Horizontal distance between the centres of two neighbouring controls on one bar: a
        /// control's full width plus the gap between them, in layout X units.
        /// </summary>
        private static float ControlPitch(float aspectX)
        {
            return ControlHalfWidth(aspectX) * 2f + VgsConstants.PortControlMargin * aspectX;
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

        /// <summary>
        /// Number of bars of one direction that are actually drawn on this node: event ports for
        /// an output, <see cref="Node.InputBars"/> for an input (ADR-0050).
        ///
        /// The two are counted from different lists on purpose — an event port is one bar, an
        /// action port is one bar *per request* — and every stack-height, culling and hit-test
        /// rule reads this one number, so the two kinds cannot disagree about how tall the
        /// column is.
        /// </summary>
        public static int DrawnPortCount(Node node, PortDirection direction)
        {
            if (node == null) return 0;

            int count = 0;

            if (direction == PortDirection.Output)
            {
                System.Collections.Generic.List<Port> events = node.Events;
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Slot >= 0) count++;
                }
                return count;
            }

            System.Collections.Generic.List<InputBar> bars = node.InputBars;
            for (int i = 0; i < bars.Count; i++)
            {
                if (bars[i].Slot >= 0) count++;
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
                || HitAnyInputBar(node, graphPoint)
                || HitAnyWatchBar(node, graphPoint);
        }

        /// <summary>
        /// True when the point is on one of the node's drawn input bars (ADR-0050). Input bars
        /// are hit through their own rectangles, because a port no longer has one.
        /// </summary>
        private static bool HitAnyInputBar(Node node, Vector2 graphPoint)
        {
            List<InputBar> bars = node.InputBars;
            for (int i = 0; i < bars.Count; i++)
            {
                Vector2 topLeft, size;
                if (!TryInputBarRect(node, bars[i], out topLeft, out size)) continue;
                if (Contains(topLeft, size, graphPoint)) return true;
            }
            return false;
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


