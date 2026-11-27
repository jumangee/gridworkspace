using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using Vgs.Core;
using Vgs.Game;
using Vgs.Model;
using static Vgs.Ui.VgsPalette;

namespace Vgs.Ui
{
    /// <summary>
    /// Drawing: the canvas, the cards, the port bars, the group frames, the lines and the
    /// chrome. Nothing here reads the world — every value comes from the graph or from a
    /// cached string (ADR-0003).
    ///
    /// A partial of <see cref="VgsEditorScreen"/>.
    /// </summary>
    public sealed partial class VgsEditorScreen
    {
        // ------------------------------------------------------------------ drawing

        protected override void OnDraw(float transitionAlpha)
        {
            try
            {
                // The layout below is in client-area coordinates and every primitive converts
                // at draw time, so the transform has to be current before the first one. It is
                // a per-frame cache, not a constant: the safe area changes with the resolution
                // (ADR-0009).
                VgsScreenDraw.SyncScreenSpace();

                Vector2 canvasTopLeft, canvasSize;
                CanvasRect.Get(out canvasTopLeft, out canvasSize);

                DrawBackdrop(canvasTopLeft, canvasSize);
                DrawGrid(canvasTopLeft, canvasSize);

                // Order matters: grid borders are the outermost backdrop, then group frames for
                // their members' cards; then lines, so a connection never crosses a block's text;
                // then cards, then the port bars of both kinds of node, so a bar is never
                // hidden under a line that ends on its dot.
                //
                // Grid before group is the nesting order of the construct itself (ADR-0032): a
                // grid contains groups, groups contain cards. Drawing them the other way round
                // would paint a group's translucent fill over its own grid's border.
                DrawGridBorders(canvasTopLeft, canvasSize);
                DrawGroupFrames(canvasTopLeft, canvasSize);

                // The conveyor network is the physical substrate the logic sits on: it is drawn
                // under the cards and under the logical connections, so a wire always reads on
                // top of a pipe it happens to share a lane with (ADR-0040).
                DrawConveyors(canvasTopLeft, canvasSize);

                DrawEdges(canvasTopLeft, canvasSize);
                DrawWatchLines(canvasTopLeft, canvasSize);
                DrawPendingLine(canvasTopLeft, canvasSize);
                DrawPendingWatchLine(canvasTopLeft, canvasSize);
                DrawNodes(canvasTopLeft, canvasSize);
                DrawGroupPorts(canvasTopLeft, canvasSize);

                DrawBottomBar();
                DrawHeader();

                if (_menuTarget != null) DrawActionMenu();

                // The modes popup hangs above the bar's top edge and overlaps the canvas, so it
                // is drawn after everything it covers.
                if (_modesMenuOpen) DrawModesMenu();
            }
            catch (Exception e)
            {
                // Swallowing here keeps the screen alive so the user can still close it.
                VgsLog.Throttled("screendraw", "editor draw failed", e, 300);
            }
        }

        private static void DrawBackdrop(Vector2 topLeft, Vector2 size)
        {
            VgsScreenDraw.Rect(Vector2.Zero, new Vector2(1f, 1f), ColBackdrop);
            VgsScreenDraw.Rect(topLeft, size, ColCanvas);
        }

        /// <summary>
        /// Draws the "infinite" field. The spacing is derived from zoom so the grid stays
        /// legible at any scale; lines are skipped when they would be denser than a few
        /// pixels, which is what keeps zoomed-out views cheap.
        ///
        /// <b>Every line is one device pixel thick at every zoom, and the two kinds of line
        /// differ in colour only</b> (ADR-0032 §3). A thickness in layout units cannot express
        /// that: the axes do not share a scale (ADR-0009), so the same value is one pixel on one
        /// axis and <c>height/width</c> of a pixel on the other, and it would also have to be
        /// divided by the zoom to stay constant. The two effects together are what made the
        /// lighter every-fifth line look like a second, thicker kind of line.
        /// </summary>
        private void DrawGrid(Vector2 topLeft, Vector2 size)
        {
            float step = VgsConstants.BaseCellSize * _view.Zoom;

            // Below this the lines merge into a solid wash; draw nothing instead.
            const float minVisibleStep = 0.006f;
            if (step < minVisibleStep) return;

            // Every 5th line is emphasised to give a sense of scale — in colour, not in width.
            const int majorEvery = 5;

            float startX = -_view.Camera.X * _view.Zoom;
            int firstCol = (int)Math.Floor(-startX / step);

            for (int i = 0; ; i++)
            {
                int col = firstCol + i;
                float x = startX + col * step;
                if (x > size.X) break;
                if (x < 0f) continue;

                Color c = (col % majorEvery == 0) ? ColGridMajor : ColGridMinor;
                VgsScreenDraw.VerticalLinePixels(topLeft.X + x, topLeft.Y, topLeft.Y + size.Y, c,
                                                 VgsConstants.GridLinePixels);
            }

            float startY = -_view.Camera.Y * _view.Zoom;
            int firstRow = (int)Math.Floor(-startY / step);

            for (int i = 0; ; i++)
            {
                int row = firstRow + i;
                float y = startY + row * step;
                if (y > size.Y) break;
                if (y < 0f) continue;

                Color c = (row % majorEvery == 0) ? ColGridMajor : ColGridMinor;
                VgsScreenDraw.HorizontalLinePixels(topLeft.Y + y, topLeft.X, topLeft.X + size.X, c,
                                                   VgsConstants.GridLinePixels);
            }
        }

        /// <summary>
        /// Draws the conveyor network as pipes between the cards it joins (ADR-0040).
        ///
        /// <b>What a link means here.</b> Not "these two blocks are somewhere on the same
        /// conveyor network" — on a cargo ship that would be every pair of containers — but
        /// "these two are adjacent on the network with no other <em>drawn</em> block between
        /// them" (see <see cref="ConveyorNetwork"/>). The picture is therefore a graph of the
        /// cards, not a picture of the tubes: a junction that has no card is invisible and its
        /// neighbours end up directly joined, which is exactly the rule the requirement asks
        /// for — the points of intersection are the blocks already on the schema.
        ///
        /// <b>Where the pipe attaches.</b> To the card's right edge on the source side and its
        /// left edge on the target side, the same sides a connection uses, so the two kinds of
        /// line leave a card the same way. The route is the ordinary orthogonal one
        /// (<see cref="EdgeRouting.Build"/>), and the pipe is its two rails
        /// (<see cref="PipeRouter.Build"/>).
        ///
        /// <b>Drawn before the cards, after the frames.</b> A pipe is plumbing: the card is what
        /// the player acts on and the logical line is what he reads, so both are painted over
        /// it. The ends of the route lie inside the cards and are hidden by them, which is what
        /// makes the pipe look like it enters the block rather than stopping at its border.
        ///
        /// The per-frame budget is <see cref="VgsConstants.MaxRenderedConveyors"/>: a pipe with
        /// one bend costs about seventy rectangles (two rails per straight, plus a staircase of
        /// two pieces per step per bend), so the cap is what keeps a fully plumbed cargo deck
        /// from turning the canvas into a texture. Links past the cap stay in the graph and are
        /// not drawn, exactly like connections past <see cref="VgsConstants.MaxRenderedEdges"/>.
        /// </summary>
        private void DrawConveyors(Vector2 topLeft, Vector2 size)
        {
            if (!_view.ShowConveyors) return;

            IList<ConveyorLink> links = _graph.Conveyors;
            if (links.Count == 0) return;

            float halfWidth = VgsConstants.ConveyorHalfWidth * _view.Zoom;
            if (halfWidth <= 0f) return;

            float corner = VgsConstants.ConveyorCorner * _view.Zoom;
            float step = VgsConstants.ConveyorStep * _view.Zoom;
            float aspect = VgsScreenDraw.ClientAspect;

            int drawn = 0;
            for (int i = 0; i < links.Count && drawn < VgsConstants.MaxRenderedConveyors; i++)
            {
                ConveyorLink link = links[i];
                if (link == null) continue;

                Node source = _graph.GetNode(link.SourceEntityId);
                Node target = _graph.GetNode(link.TargetEntityId);

                // A link whose card is gone is skipped for one refresh: the pass that dropped
                // the node has already rebuilt the network, and until then the honest answer is
                // to draw nothing rather than a pipe to nowhere.
                if (source == null || target == null) continue;

                Color colour = Fade(ColConveyorPipe, MaxAlpha(source, target));
                if (colour.A == 0) continue;

                Vector2 from, to;
                if (!PipeAnchor(source, true, out from)) continue;
                if (!PipeAnchor(target, false, out to)) continue;

                EdgeRouting.Build(GraphToScreen(from, topLeft), GraphToScreen(to, topLeft),
                                  VgsConstants.LineGutter * _view.Zoom, _route);

                if (_pipeRouter.Build(_route, corner, halfWidth, aspect, step, _pipeBuffer) == 0) continue;

                for (int p = 0; p < _pipeBuffer.Count; p++)
                {
                    Vector4 piece = _pipeBuffer[p];
                    VgsScreenDraw.SegmentPixels(new Vector2(piece.X, piece.Y),
                                                new Vector2(piece.Z, piece.W),
                                                VgsConstants.ConveyorWallPixels,
                                                topLeft, size, colour);
                }
                drawn++;
            }
        }

        /// <summary>
        /// The point a pipe leaves a card from, in graph units: the middle of the card's right
        /// edge when <paramref name="trailingEdge"/> is true (the source), and the middle of its
        /// left edge otherwise (the target). Both kinds of line therefore leave a card on the
        /// right and arrive on the left, and a reader never has to work out which convention a
        /// particular line follows.
        /// </summary>
        private static bool PipeAnchor(Node node, bool trailingEdge, out Vector2 anchor)
        {
            anchor = Vector2.Zero;

            Vector2 cardTopLeft, cardSize;
            PortGeometry.CardRect(node, out cardTopLeft, out cardSize);
            if (cardSize.X <= 0f || cardSize.Y <= 0f) return false;

            float x = trailingEdge ? cardTopLeft.X + cardSize.X : cardTopLeft.X;
            anchor = new Vector2(x, cardTopLeft.Y + cardSize.Y * 0.5f);
            return true;
        }

        /// <summary>
        /// Draws every connection as a smooth line from an output dot to an input dot.
        ///
        /// The shape is still routed orthogonally — that is what makes every line arrive at its
        /// target from the left, moving right — but what is drawn is the rounded path derived
        /// from that route, not the route itself (ADR-0046): a straight run to the middle of the
        /// first segment, a bend to the middle of each following one, a straight into the dot.
        ///
        /// It walks <see cref="GridGraph.Lines"/>, not <see cref="GridGraph.Edges"/>: the same
        /// event may drive the same action several times (one toolbar page each), and those
        /// repeats are separate connections but the same line between the same two dots. The
        /// de-duplication is done once by <see cref="GridGraph.LayoutPorts"/>, so this per-frame
        /// path stays a single pass.
        ///
        /// A connection whose ports have no drawing slot (the block has more ports than
        /// <see cref="GridGraph.MaxPortsPerSide"/>) is skipped: it exists in the graph but has
        /// no dot to attach to, and drawing it from nowhere would be a lie.
        /// </summary>
        private void DrawEdges(Vector2 topLeft, Vector2 size)
        {
            IList<Edge> lines = _graph.Lines;
            int drawn = 0;

            for (int i = 0; i < lines.Count && drawn < VgsConstants.MaxRenderedEdges; i++)
            {
                Edge edge = lines[i];

                Node source = _graph.GetNode(edge.SourceEntityId);
                Node target = _graph.GetTargetNode(edge);
                if (source == null || target == null) continue;

                Port outPort = source.FindEvent(edge.SourcePortKey);
                Port inPort = target.FindAction(edge.TargetPortKey);
                if (outPort == null || inPort == null) continue;

                Vector2 from, to;
                float aspect = VgsScreenDraw.ClientAspect;
                if (!PortGeometry.TryPortDot(source, outPort, aspect, out from)) continue;
                if (!PortGeometry.TryPortDot(target, inPort, aspect, out to)) continue;

                // A line belongs to both of its ends: it is a ghost only when *both* are excluded
                // by the search, so a matching block keeps its wiring visible — otherwise the
                // player could not tell what the block they found is connected to (ADR-0023).
                Color colour = Fade(ColLine, MaxAlpha(source, target));
                Vector2 start = GraphToScreen(from, topLeft);
                Vector2 end = GraphToScreen(to, topLeft);

                // A connection whose ends are one card — a block driving itself (ADR-0036) —
                // cannot use the ordinary route: its middle run would fall on the seam between
                // the port bars, which are drawn over the lines, and the wire would read as two
                // disconnected stubs. It loops around the card instead.
                if (ReferenceEquals(source, target))
                {
                    Vector2 cardTopLeft, cardSize;
                    PortGeometry.CardRect(source, out cardTopLeft, out cardSize);
                    DrawSelfRoute(start, end, GraphToScreen(cardTopLeft, topLeft),
                                  cardSize * _view.Zoom, colour, topLeft, size);
                }
                else
                {
                    DrawRoute(start, end, colour, topLeft, size);
                }
                drawn++;
            }
        }

        /// <summary>
        /// Draws the live line from the source card to the pointer while binding.
        ///
        /// It starts at the dot the connection *will* get, not at the pointer's origin, so the
        /// moment the connection is made nothing jumps: the port bar appears exactly where the
        /// line already was.
        /// </summary>
        private void DrawPendingLine(Vector2 topLeft, Vector2 size)
        {
            if (_bindSource == null || _bindPort == null || !_mouseValid) return;

            Vector2 from;
            if (!PortGeometry.TryPendingSourceDot(_bindSource, _bindPort,
                                                  VgsScreenDraw.ClientAspect, out from)) return;

            Vector2 start = GraphToScreen(from, topLeft);
            Vector2 end = _mouse;

            DrawRoute(start, end, ColLinePending, topLeft, size);

            // A dot under the cursor makes the line read as "attached to the pointer" rather
            // than as a line that happens to end there.
            VgsScreenDraw.Disc(end, VgsConstants.PortControlRadius * _view.Zoom, topLeft, size, ColLinePending);
        }

        /// <summary>
        /// Draws every watch link: a dotted line from a watch port's dot to the watched block's
        /// card (ADR-0017).
        ///
        /// It ends at the card's centre, not at a port: the watched block has no "observe me"
        /// port, and a line into one of its real ports would claim a call the controller never
        /// makes. Cards are drawn after lines, so the visible end of the line is the card's
        /// border — which reads as "the line ends in the block".
        ///
        /// Dotted, unlike a connection, because the two mean different things: a solid line is
        /// "this fires that", a dotted one is "this watches that". The dots are a fixed number of
        /// device pixels (ADR-0046 §3), not a fraction of the graph, so the pattern stays the
        /// same at every zoom — a pattern that thinned out as the player zoomed would stop
        /// distinguishing a watch from a connection exactly where the schema gets busy.
        /// </summary>
        private void DrawWatchLines(Vector2 topLeft, Vector2 size)
        {
            float aspect = VgsScreenDraw.ClientAspect;

            IList<Node> nodes = _graph.Nodes;
            int drawn = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                Node controller = nodes[i];
                // Only a controller has watch bars; the state object is the flag (ADR-0030).
                if (controller.Controller == null) continue;

                List<WatchTarget> watched = controller.Controller.Watched;
                if (watched.Count == 0) continue;

                for (int w = 0; w < watched.Count; w++)
                {
                    // The same cap a connection line obeys: a canvas full of controllers must not
                    // be able to queue an unbounded number of dash quads per frame.
                    if (drawn >= VgsConstants.MaxRenderedEdges) return;

                    WatchTarget watch = watched[w];
                    if (watch.Slot < 0) continue;

                    Vector2 from;
                    if (!PortGeometry.TryWatchDot(controller, watch, aspect, out from)) continue;

                    // A controller watching itself has a bar but no line (ADR-0036): the rule is
                    // PortGeometry.ShouldDrawWatchLine's, so the offline gate can assert it.
                    if (!PortGeometry.ShouldDrawWatchLine(controller, watch)) continue;

                    Node target = _graph.GetNode(watch.EntityId);
                    Vector2 to;

                    if (target == null || !PortGeometry.TryCardCentre(target, out to))
                    {
                        // The watched block is hidden from the schema or gone: the port still
                        // exists (the list is the player's data), but there is nowhere on the
                        // canvas to draw its line to.
                        continue;
                    }

                    DrawDottedRoute(GraphToScreen(from, topLeft), GraphToScreen(to, topLeft),
                                    Fade(ColWatchLine, MaxAlpha(controller, target)),
                                    topLeft, size);
                    drawn++;
                }
            }
        }

        /// <summary>
        /// Draws the watch line from the controller's bell to the pointer while a watch is being
        /// attached, and the red refusal flash (ADR-0017 §5).
        ///
        /// The line starts at the button's centre because the port does not exist yet: the point
        /// the line leaves from is the point the player pressed, and the moment the port appears
        /// the line moves to its dot.
        /// </summary>
        private void DrawPendingWatchLine(Vector2 topLeft, Vector2 size)
        {
            if (_watchSource == null || !_mouseValid) return;

            Vector2 from;
            if (!PortGeometry.TryWatchButton(_watchSource, VgsScreenDraw.ClientAspect, out from)) return;

            bool refused = _screenFrame < _watchErrorUntilFrame;
            Color colour = refused ? ColWatchError : ColWatchLine;

            Vector2 start = GraphToScreen(from, topLeft);
            Vector2 end = _mouse;

            DrawDottedRoute(start, end, colour, topLeft, size);

            VgsScreenDraw.Disc(end, VgsConstants.PortControlRadius * _view.Zoom, topLeft, size, colour);
        }

        /// <summary>
        /// Draws a watch line: the same rounded path a connection uses, drawn with the watch
        /// line's own rhythm — <see cref="VgsConstants.WatchDotPixels"/> of ink every
        /// <see cref="VgsConstants.WatchDotPixels"/> + <see cref="VgsConstants.WatchDotGapPixels"/>
        /// of length (ADR-0017, ADR-0047).
        ///
        /// The route comes from <see cref="EdgeRouting.Build"/>, the same router a connection
        /// uses, so a watch line bends and arrives exactly like a wire; the dots are placed along
        /// the rounded path, so the rhythm runs through a bend instead of restarting at every
        /// segment.
        /// </summary>
        private void DrawDottedRoute(Vector2 from, Vector2 to, Color color,
                                     Vector2 clipTopLeft, Vector2 clipSize)
        {
            EdgeRouting.Build(from, to, VgsConstants.LineGutter * _view.Zoom, _route);
            EdgeRouting.Round(_route, PixelsPerUnit, _path);
            VgsScreenDraw.PathDots(_path, VgsConstants.WatchDotPixels,
                                   VgsConstants.WatchDotGapPixels,
                                   clipTopLeft, clipSize, color);
        }

        private void DrawRoute(Vector2 from, Vector2 to, Color color, Vector2 clipTopLeft, Vector2 clipSize)
        {
            // The dots are already in screen space, so the gutter has to be too. It is a graph
            // constant scaled by zoom, which keeps the clearance between a dot and its vertical
            // run the same fraction of a card at every zoom level (ADR-0010).
            EdgeRouting.Build(from, to, VgsConstants.LineGutter * _view.Zoom, _route);
            DrawRoundedRoute(color, clipTopLeft, clipSize);
        }

        /// <summary>
        /// Draws a connection that starts and ends on the same card: the loop of ADR-0036.
        ///
        /// The card's rectangle is passed in screen space because that is what the loop goes
        /// around — its lanes are measured from the card's edges, not from the dots, so they stay
        /// outside the card at every zoom (<see cref="EdgeRouting.BuildSelfLoop"/>). The route
        /// itself stays a pure function, so it is asserted offline like every other route.
        /// </summary>
        private void DrawSelfRoute(Vector2 from, Vector2 to, Vector2 cardTopLeftScreen,
                                   Vector2 cardSizeScreen, Color color,
                                   Vector2 clipTopLeft, Vector2 clipSize)
        {
            EdgeRouting.BuildSelfLoop(from, to, cardTopLeftScreen, cardSizeScreen,
                                      VgsConstants.LineGutter * _view.Zoom, _route);
            DrawRoundedRoute(color, clipTopLeft, clipSize);
        }

        /// <summary>
        /// Draws the route the last build produced as the line a connection is drawn as: every
        /// corner is replaced by a bend from the middle of one segment to the middle of the next
        /// (<see cref="EdgeRouting.Round"/>), and the result is a dense rhythm of dots —
        /// <see cref="VgsConstants.LineDotPixels"/> of ink every
        /// <see cref="VgsConstants.LineDotPixels"/> + <see cref="VgsConstants.LineDotGapPixels"/>
        /// of length (ADR-0046, ADR-0047).
        ///
        /// <b>Why dots and not a solid line.</b> The screen-space API has no antialiasing, so a
        /// solid two-pixel line has a hard edge that the eye reads as heavier than two pixels; the
        /// one-pixel gaps are averaged into it and the line reads at its true weight. The ink
        /// height is unchanged — this is a lighter <em>edge</em>, not a thinner line.
        ///
        /// One place for both shapes — the ordinary route and the self-loop — so the bend shape,
        /// the rhythm and the clipping cannot drift between them.
        /// </summary>
        private void DrawRoundedRoute(Color color, Vector2 clipTopLeft, Vector2 clipSize)
        {
            EdgeRouting.Round(_route, PixelsPerUnit, _path);
            VgsScreenDraw.PathDots(_path, VgsConstants.LineDotPixels,
                                   VgsConstants.LineDotGapPixels,
                                   clipTopLeft, clipSize, color);
        }

        /// <summary>
        /// The client area in pixels, which is what the rounded path has to be sampled in
        /// (ADR-0009): one X unit of the layout is the window's width and one Y unit its height,
        /// so a sample count expressed in layout units would flatten a bend on a wide window as
        /// coarsely as on a square one.
        /// </summary>
        private static Vector2 PixelsPerUnit
        {
            get { return new Vector2(VgsScreenDraw.ClientWidth, VgsScreenDraw.ClientHeight); }
        }

        private void DrawNodes(Vector2 topLeft, Vector2 size)
        {
            IList<Node> nodes = _graph.Nodes;
            if (nodes.Count == 0)
            {
                // A grid with no visible block is only "empty" when it has no visible group
                // either: a group whose members are all hidden is not in the graph at all, so
                // the two counts are both zero exactly when there is nothing to draw.
                if (_graph.GroupCount == 0)
                {
                    VgsScreenDraw.TextCentered(VgsText.EmptyGrid, topLeft + size * 0.5f, 0.8f, ColTextDim);
                    return;
                }
            }

            // Cheap frustum culling: only draw what is on screen. On a large ship this is
            // the difference between a smooth canvas and a slideshow. There is deliberately no
            // "shown N of M" notice for the blocks this drops: the header already carries the
            // totals, and a line under the canvas that appears and disappears as the player zooms
            // was noise on top of a picture that is self-explanatory (ADR-0023).
            //
            // The same predicate answers "which cards are visible" for the state pass
            // (CollectVisibleNodeIds): one definition of visible, so the values the player sees
            // are refreshed for exactly the cards the player sees.
            int drawn = 0;
            for (int i = 0; i < nodes.Count && drawn < VgsConstants.MaxRenderedBlocks; i++)
            {
                Node n = nodes[i];
                if (!IsCardOnCanvas(n, topLeft, size)) continue;

                DrawNode(n, GraphToScreen(n.Position, topLeft), topLeft, size);
                drawn++;
            }
        }

        /// <summary>
        /// Draws one card: name, its localized type, the optional status line, its button row and
        /// its port bars.
        ///
        /// The card is deliberately just a label. Ports are drawn *outside* it, as bars
        /// hanging underneath, and only the ones the user has actually connected — which is
        /// what keeps a block with forty terminal actions the same size as a block with four
        /// (see <see cref="PortGeometry"/>).
        /// </summary>
        private void DrawNode(Node node, Vector2 screenPos, Vector2 clipTopLeft, Vector2 clipSize)
        {
            bool selected = ReferenceEquals(node, _selected);
            bool hovered = ReferenceEquals(node, _hoverNode);

            // A card the search excludes is still drawn — as a ghost at
            // VgsConstants.SearchDimOpacity — so the player keeps the shape of the whole schema
            // while looking for one block (ADR-0023). Every colour of the card and of everything
            // hanging off it goes through this one multiplier.
            float alpha = AlphaOf(node);

            Vector2 cardSize = new Vector2(VgsConstants.NodeWidth, PortGeometry.CardHeight(node)) * _view.Zoom;

            // The card is a single filled rectangle. The port bars are separate rectangles
            // below it and must *not* be covered by this fill, or the stack would read as one
            // tall block instead of as ports attached to the card's bottom edge.
            VgsScreenDraw.RectClipped(screenPos, cardSize, clipTopLeft, clipSize, Fade(ColNode, alpha));

            // Damage, drawn over the card's fill and under everything the player reads on it:
            // the border, the labels, the buttons and the port bars all come later. Half-opacity
            // red from the right edge inward, as wide as the damage is — see
            // PortGeometry.TryDamageRect for why it is the damage and not the health (ADR-0028).
            //
            // <b>The width is the damage, with no threshold.</b> 1% of damage paints 1% of the
            // card and 7% paints 7%: there is no "visible from N%" step, and the only value that
            // draws nothing is a block at full integrity. The single adjustment is a floor of one
            // screen pixel, and it is expressed in the same units it is compared against — see
            // PortGeometry.FitDamageBand, which also clamps the band to the card. The first
            // version of this code compared a *normalized* width against 1f, i.e. against the
            // whole screen, so the comparison was always true and every damaged card painted a
            // band a full screen wide. Found in game.
            Vector2 damageTopLeft, damageSize;
            if (PortGeometry.TryDamageRect(node, out damageTopLeft, out damageSize))
            {
                // One screen pixel in graph units. Normalized X is graph X times the zoom, and a
                // pixel is 1/clientWidth of the normalized width, so the conversion goes through
                // both. Zero when the screen has not been measured yet, which leaves the band
                // exactly as the geometry computed it.
                float onePixel = 0f;
                if (VgsScreenDraw.ClientWidth > 0f && _view.Zoom > 0f)
                {
                    onePixel = 1f / (VgsScreenDraw.ClientWidth * _view.Zoom);
                }

                PortGeometry.FitDamageBand(
                    ref damageTopLeft, ref damageSize, node.Position,
                    new Vector2(VgsConstants.NodeWidth, PortGeometry.CardHeight(node)),
                    onePixel);

                VgsScreenDraw.RectClipped(
                    screenPos + (damageTopLeft - node.Position) * _view.Zoom,
                    damageSize * _view.Zoom, clipTopLeft, clipSize, Fade(ColDamage, alpha));
            }

            Color border = selected ? ColNodeSelected : (hovered ? ColNodeHover : ColNodeBorder);
            border = Fade(border, alpha);
            VgsScreenDraw.BorderClipped(screenPos, cardSize, clipTopLeft, clipSize, border,
                                        selected ? Hairline * 2f : Hairline);

            float zoomText = CardTextScale;
            float pad = cardSize.X * PortGeometry.CardTextPaddingRatio;
            float textRight = cardSize.X - pad;
            float textLeft = pad;

            // The card's buttons occupy the right end of the card — all of them, in one row
            // (ADR-0020) — so the labels stop before the row instead of running underneath
            // icons that are drawn on top of them.
            //
            // The span comes from `PortGeometry`, in graph units, and it is already
            // aspect-corrected there: a button is round on *screen*, so its horizontal extent is
            // radius × aspect, and the gap before the text is corrected the same way. Reserving
            // the vertical radius would leave the text running under the icons on every window
            // that is not square.
            float spanLeft, spanRight;
            if (PortGeometry.TryCardTextSpan(node, VgsScreenDraw.ClientAspect, out spanLeft, out spanRight))
            {
                textLeft = (spanLeft - node.Position.X) * _view.Zoom;
                textRight = (spanRight - node.Position.X) * _view.Zoom;
            }
            else if (PortGeometry.CardButtonCount(node) > 0)
            {
                // The row fills the card and the span has collapsed: this is the portrait window
                // the four-button row cannot leave room in (ADR-0021 §Consequences). Drawing the
                // name anyway would put it *under* the icons, which reads as a rendering fault;
                // a card that shows its controls and no text is the honest degradation.
                textRight = textLeft;
            }

            float textWidth = textRight - textLeft;
            if (textWidth < 0f) textWidth = 0f;

            // Lines are placed by dividing the card's height into as many bands as it has
            // lines, rather than by fixed fractions of the card: a controller's card carries a
            // third line, and fixed fractions would leave that line half outside its own band.
            // The extra line is reserved for a controller whether or not its description is
            // known yet, and "is a controller" is the controller state being present (ADR-0030).
            ControllerState controller = node.Controller;
            int lines = controller != null ? 3 : 2;
            float band = cardSize.Y / lines;

            // A zero span means the row has filled the card (the portrait window of ADR-0021
            // §Consequences). The lines are skipped rather than drawn with maxWidth = 0: what a
            // renderer does with a zero width is not something this project has verified, and
            // "the name is not drawn" is a claim the ADR makes and the acceptance list checks.
            if (textWidth > 0f)
            {
                VgsScreenDraw.Text(node.DisplayName,
                                   screenPos + new Vector2(textLeft, band * 0.10f),
                                   VgsConstants.NodeTitleScale * zoomText, Fade(ColText, alpha),
                                   textWidth);

                // The type line, in the language the game is running in (ADR-0043). It used to be
                // `SubtypeId`, which is the engine's identifier: "LargeBlockTimer" in every
                // language, and "Unknown" for the game's legacy definitions whose subtype is
                // empty. The localized name is the same string the search matches and the
                // terminal shows, so the card and the search cannot disagree about what a block
                // is called.
                VgsScreenDraw.Text(node.TypeDisplayName,
                                   screenPos + new Vector2(textLeft, band * 1.10f),
                                   VgsConstants.NodeSubtitleScale * zoomText, Fade(ColTextDim, alpha),
                                   textWidth);

                // The mode description: what an event controller actually checks. A third line, in
                // the watch colour, because it is the block's *state* rather than its identity —
                // and it is the only place the player can see the threshold without the terminal.
                if (controller != null && !string.IsNullOrEmpty(controller.StatusText))
                {
                    VgsScreenDraw.Text(controller.StatusText,
                                       screenPos + new Vector2(textLeft, band * 2.10f),
                                       VgsConstants.NodeStatusScale * zoomText, Fade(ColWatch, alpha),
                                       textWidth);
                }
            }

            // The alpha is computed once here and passed down. Recomputing it inside
            // DrawPortBar and DrawCardButton would run the search rule once per port and once
            // per button of the card, on every frame — same picture, a dozen times the string
            // scans.
            DrawPortBars(node, border, alpha, clipTopLeft, clipSize);

            // The card's buttons are drawn last so they are never covered by a port bar of a
            // neighbouring card that happens to be drawn later.
            DrawCardButtons(node, alpha, clipTopLeft, clipSize);

            // The power switch goes last of all, for the same reason and one more: it sits in the
            // card's state column, on the leading edge, and a port bar of a card drawn later can
            // overlap it — the switch has to win, because a control the player cannot see is a
            // control they cannot use.
            //
            // The widget is drawn first, inside the same column: it is a reading rather than a
            // control, and the switch is the control (ADR-0042).
            DrawStateWidget(node, alpha, clipTopLeft, clipSize);
            DrawPowerSwitch(node, alpha, clipTopLeft, clipSize);
        }

        /// <summary>
        /// Draws the block's <b>state widget</b> in the lower band of the card's state column
        /// (ADR-0042): a bar filled from the bottom up, a pair of narrow bars, or a two-handed clock.
        ///
        /// <b>Nothing is drawn when there is nothing to read.</b> Three conditions, and each is a
        /// different fact:
        ///
        ///  * the card has no widget at all — the category publishes no reading, so the column has
        ///    nothing in its lower band;
        ///  * the block is switched off — the requirement's own rule, and the reason it is checked
        ///    rather than left to the geometry: a switched-off block's fill is still whatever is
        ///    inside it, but showing it would suggest the block is doing something with it. The
        ///    check asks <see cref="Node.CanBeEnabled"/> first, because a cargo container has no
        ///    switch and its <see cref="BlockPower"/> is therefore never anything but unknown;
        ///  * the block reports no reading — not read yet, or (a timer) not counting down.
        ///
        /// <b>All three are one function, and it is not here.</b> <see cref="StateWidget.ShowsWidget"/>
        /// in the model owns the rule, so the offline gate can pin every combination of its inputs;
        /// a rule that lives only in this file is a rule nothing can check (ADR-0042 §5).
        ///
        /// <b>The column keeps its width in all three cases</b>, because the width comes from the
        /// block's type (<see cref="PortGeometry.StateColumnWidth"/>). Nothing on the card moves when
        /// the widget appears, disappears or fills up.
        ///
        /// <b>One base colour for every element.</b> The shape carries the value, so colouring the
        /// fill by the number would be a second scale for the same fact. The one exception is the
        /// battery's bolt, which says a <em>direction</em> rather than an amount and is the only
        /// accent in the widget (ADR-0042 §2).
        /// </summary>
        private void DrawStateWidget(Node node, float alpha, Vector2 clipTopLeft, Vector2 clipSize)
        {
            // One question, asked once: whether the widget is drawn at all is a model rule with
            // three conditions that look alike and mean different things, and it lives in
            // StateWidget.ShowsWidget so that the offline gate can pin every combination of its
            // inputs (ADR-0042 §5). The draw path only asks it — no second test of the same fact,
            // which is what the rule was moved out of this file to avoid.
            if (!StateWidget.ShowsWidget(node.Widget, node.CanBeEnabled, node.State.Power,
                                         node.State.WidgetActive))
            {
                return;
            }

            Color colour = Fade(ColWidget, alpha);
            float aspect = VgsScreenDraw.ClientAspect;

            if (!StateWidget.IsBar(node.Widget))
            {
                DrawStateClock(node, colour, aspect, clipTopLeft, clipSize);
                return;
            }

            int bars = StateWidget.BarCount(node.Widget);
            for (int i = 0; i < bars; i++)
            {
                Vector2 frameTopLeft, frameSize, fillTopLeft, fillSize;
                if (!PortGeometry.TryStateWidgetBar(node, aspect, i, out frameTopLeft, out frameSize,
                                                    out fillTopLeft, out fillSize))
                {
                    continue;
                }

                // The frame is a border, not a fill: an empty container has to be a visible empty
                // rectangle, and that rectangle is what tells the player the reading exists.
                Vector2 frameScreen = GraphToScreen(frameTopLeft, clipTopLeft);
                Vector2 frameSizeScreen = frameSize * _view.Zoom;

                VgsScreenDraw.BorderClipped(frameScreen, frameSizeScreen, clipTopLeft, clipSize,
                                            colour, Hairline);

                if (fillSize.Y <= 0f || fillSize.X <= 0f) continue;

                VgsScreenDraw.RectClipped(GraphToScreen(fillTopLeft, clipTopLeft),
                                          fillSize * _view.Zoom, clipTopLeft, clipSize, colour);
            }

            if (node.Widget == StateWidgetKind.Battery)
            {
                DrawStateBolt(node, colour, alpha, aspect, clipTopLeft, clipSize);
            }
        }

        /// <summary>
        /// The battery's charge-mode bolt, beside its bar: yellow while recharging, red while
        /// discharging, nothing in <c>Auto</c> (ADR-0042 §4).
        ///
        /// <b>The accent is a second colour on a widget that has exactly one.</b> Every other element
        /// of every widget is <see cref="ColWidget"/>; this one says which way the charge is going,
        /// which is the only thing a battery knows that its charge fraction does not. <c>Auto</c>
        /// draws nothing rather than a third colour: "neither direction" is not a state of the
        /// battery, it is the absence of one.
        /// </summary>
        private void DrawStateBolt(Node node, Color colour, float alpha, float aspect,
                                   Vector2 clipTopLeft, Vector2 clipSize)
        {
            WidgetAccent accent = node.State.Accent;
            if (accent == WidgetAccent.None) return;

            Vector2 center;
            float inkHeight;
            if (!PortGeometry.TryStateWidgetBolt(node, aspect, out center, out inkHeight)) return;

            Color boltColour = accent == WidgetAccent.Recharge
                ? ColBatteryRecharge
                : ColBatteryDischarge;

            Vector2 centerScreen = GraphToScreen(center, clipTopLeft);
            float ink = inkHeight * _view.Zoom;

            if (VgsScreenDraw.Icon(VgsIcon.Bolt, centerScreen, ink, Fade(boltColour, alpha),
                                   clipTopLeft, clipSize))
            {
                return;
            }

            // The atlas is unavailable: the accent degrades to the glyph it would have carried as a
            // letter, in the same colour — the rule every icon in this editor follows (ADR-0019 §6).
            VgsScreenDraw.TextCentered(VgsText.BatteryAccentGlyph, centerScreen,
                                       VgsConstants.CardButtonGlyphScale * CardTextScale,
                                       Fade(boltColour, alpha));
        }

        /// <summary>
        /// The timer's clock: a face, a hand fixed at twelve and a hand that sweeps clockwise towards
        /// it (ADR-0042 §4).
        ///
        /// <b>The fixed hand is the trigger.</b> It marks where the moving hand will be when the
        /// timer fires, so the reading is "how far along the circle" rather than "how much of a bar".
        /// The two hands differ in length on purpose: at the start and at the end they point the same
        /// way, and the only thing that tells those two moments apart is that the short hand sits
        /// inside the long one.
        ///
        /// <b>Both hands come from one function</b> (<see cref="PortGeometry.StateWidgetHandTip"/>),
        /// which is what makes "twelve o'clock" mean the same thing for the fixed hand and for the
        /// moving one. A second angle computation is how a hand ends up pointing at the wrong hour at
        /// exactly one moment — the moment the timer fires.
        /// </summary>
        private void DrawStateClock(Node node, Color colour, float aspect, Vector2 clipTopLeft,
                                    Vector2 clipSize)
        {
            Vector2 center;
            float radius;
            if (!PortGeometry.TryStateWidgetClock(node, aspect, out center, out radius)) return;

            Vector2 centerScreen = GraphToScreen(center, clipTopLeft);
            float radiusScreen = radius * _view.Zoom;

            VgsScreenDraw.Ring(centerScreen, radiusScreen, VgsConstants.WidgetRing * _view.Zoom,
                               VgsConstants.WidgetRingSegments, clipTopLeft, clipSize, colour);

            // The fixed hand: twelve o'clock, always. It is drawn first so the moving hand, when it
            // overlaps, is drawn over it — the moving one is the reading.
            Vector2 fixedTip = PortGeometry.StateWidgetHandTip(center, radius, aspect,
                                                               VgsConstants.WidgetHourHandRatio, 0f);
            VgsScreenDraw.ThickSegment(centerScreen, GraphToScreen(fixedTip, clipTopLeft),
                                       VgsConstants.WidgetHand * _view.Zoom, clipTopLeft, clipSize,
                                       colour);

            float elapsed = node.State.GetMetric(0);
            Vector2 movingTip = PortGeometry.StateWidgetHandTip(center, radius, aspect,
                                                                VgsConstants.WidgetMinuteHandRatio,
                                                                elapsed);
            VgsScreenDraw.ThickSegment(centerScreen, GraphToScreen(movingTip, clipTopLeft),
                                       VgsConstants.WidgetHand * _view.Zoom, clipTopLeft, clipSize,
                                       colour);
        }


        /// <summary>
        /// Draws the power switch in the card's <b>state column</b> (ADR-0035): a Font Awesome
        /// <c>toggle-on</c> while the block is on, a <c>toggle-off</c> while it is off, and the
        /// same <c>toggle-off</c> in red when the block is damaged below its critical integrity
        /// and the game would refuse to run it anyway.
        ///
        /// <b>Three states, and the third is not "off".</b> Grey means "switched off, click to
        /// switch on"; red means "this block cannot be switched on at all". Both are drawn with
        /// the same icon and differ only in colour, because the icon already says "off" and
        /// colouring it is what tells the player whether the switch is still a switch.
        ///
        /// <b>Nothing is drawn before the first reading.</b> <see cref="BlockPower.Unknown"/> means
        /// the state pass has not reached this card yet; drawing "off" there would be a claim about
        /// a block nobody has read. The column keeps its width regardless — that width comes from
        /// the block's type — so the icon appears without moving the name.
        ///
        /// <b>Hover brightens the state colour instead of replacing it.</b> Every other control on
        /// the canvas warms to <see cref="ColPlusFillHot"/> under the pointer, because its colour
        /// says nothing about the block. This one's colour *is* the reading, so the feedback is a
        /// lighter shade of the same state: the player still sees what the block is while the
        /// pointer is on the control that will change it.
        ///
        /// A missing texture costs the shape, never the control: the fallback is the mark the
        /// switch carried before it became an icon — <c>|</c> for on and <c>o</c> for off, in the
        /// same colour — which is the rule every icon in this editor follows (ADR-0019 §6).
        /// </summary>
        private void DrawPowerSwitch(Node node, float alpha, Vector2 clipTopLeft, Vector2 clipSize)
        {
            BlockPower power = node.State.Power;
            if (power == BlockPower.Unknown) return;

            Vector2 topLeft, size;
            if (!PortGeometry.TryPowerSwitchRect(node, VgsScreenDraw.ClientAspect, out topLeft, out size))
            {
                return;
            }

            bool hot = ReferenceEquals(node, _hoverPowerSwitch);

            Color colour;
            VgsIcon icon;
            string glyph;

            if (power == BlockPower.On)
            {
                colour = ColPowerOn;
                icon = VgsIcon.ToggleOn;
                glyph = VgsText.PowerOnGlyph;
            }
            else if (power == BlockPower.Off)
            {
                colour = ColPowerOff;
                icon = VgsIcon.ToggleOff;
                glyph = VgsText.PowerOffGlyph;
            }
            else
            {
                colour = ColPowerUnavailable;
                icon = VgsIcon.ToggleOff;
                glyph = VgsText.PowerOffGlyph;
            }

            if (hot) colour = VgsPalette.Brighten(colour, PowerSwitchHoverLift);
            colour = Fade(colour, alpha);

            // The ink height is the band, and the ink is wider than tall, so the sprite's square
            // destination is bigger than the band by the atlas's own texture scale. `Icon` takes
            // the ink height for exactly this reason, and the rectangle above is the ink box — so
            // the drawn pill lands on the rectangle the hit test uses, not merely near it.
            Vector2 centre = GraphToScreen(topLeft + size * 0.5f, clipTopLeft);
            float ink = size.Y * _view.Zoom;

            if (VgsScreenDraw.Icon(icon, centre, ink, colour, clipTopLeft, clipSize)) return;

            VgsScreenDraw.TextCentered(glyph, centre, VgsConstants.CardButtonGlyphScale * CardTextScale,
                                       colour);
        }

        /// <summary>
        /// How far a hovered power switch is lightened towards white, as a fraction.
        ///
        /// A fifth: enough to read as "the pointer is on this" at a glance, small enough that the
        /// three state colours stay distinguishable from each other while hovered — including the
        /// dark red, which a stronger lift would turn pink and thereby break the one thing the
        /// colour is for.
        /// </summary>
        private const float PowerSwitchHoverLift = 0.20f;


        /// <summary>
        /// Draws the watch bars of one controller: the yellow bars under the card, each with the
        /// watched block's name, a dot on the right (the line leaves from there) and an "x" on
        /// the left (removes the block from the list).
        ///
        /// They are drawn with the node's port bars, not in a pass of their own, because they are
        /// part of the same column: a watch bar that was drawn after the next card would cover
        /// it. The frame colour is the card's, like every other bar of the node (ADR-0014) —
        /// only the fill differs, and that is what says "this is a watch, not a call".
        /// </summary>
        private void DrawWatchBars(Node node, Color border, float alpha, Vector2 clipTopLeft,
                                   Vector2 clipSize)
        {
            if (node.Controller == null) return;

            List<WatchTarget> watched = node.Controller.Watched;
            for (int i = 0; i < watched.Count; i++)
            {
                WatchTarget watch = watched[i];
                if (watch.Slot < 0) continue;

                Vector2 topLeft, size;
                if (!PortGeometry.TryWatchBarRect(node, watch, out topLeft, out size)) continue;

                Vector2 screenTopLeft = GraphToScreen(topLeft, clipTopLeft);
                Vector2 screenSize = size * _view.Zoom;

                VgsScreenDraw.RectClipped(screenTopLeft, screenSize, clipTopLeft, clipSize,
                                          Fade(ColWatchFill, alpha));
                VgsScreenDraw.BorderClipped(screenTopLeft, screenSize, clipTopLeft, clipSize, border,
                                            Hairline);

                Vector2 labelTopLeft, labelSize;
                if (PortGeometry.TryWatchLabelRect(node, watch, VgsScreenDraw.ClientAspect,
                                                   out labelTopLeft, out labelSize))
                {
                    Vector2 labelScreen = GraphToScreen(labelTopLeft, clipTopLeft);
                    float textHeight = PortLabelHeight;
                    if (textHeight <= 0f) textHeight = screenSize.Y * VgsConstants.PortLabelFallbackHeightRatio;

                    VgsScreenDraw.Text(watch.DisplayName,
                                       new Vector2(labelScreen.X,
                                                   PortGeometry.CentredTextTop(labelScreen.Y,
                                                                               screenSize.Y, textHeight)),
                                       VgsConstants.PortLabelScale * _view.Zoom,
                                       Fade(ColText, alpha), labelSize.X * _view.Zoom);
                }

                float radius = VgsConstants.PortControlRadius * _view.Zoom;

                Vector2 dotCentre;
                if (PortGeometry.TryWatchDot(node, watch, VgsScreenDraw.ClientAspect, out dotCentre))
                {
                    DrawPortDot(GraphToScreen(dotCentre, clipTopLeft), radius, Fade(ColWatch, alpha),
                                clipTopLeft, clipSize);
                }

                Vector2 removeCentre;
                if (PortGeometry.TryWatchRemoveButton(node, watch, VgsScreenDraw.ClientAspect,
                                                      out removeCentre))
                {
                    bool hot = ReferenceEquals(node, _hoverWatchRemoveNode)
                               && ReferenceEquals(watch, _hoverWatchRemoveTarget);

                    DrawPortRemoveButton(GraphToScreen(removeCentre, clipTopLeft),
                                         ColWatch, alpha, hot, clipTopLeft, clipSize);
                }
            }
        }

        /// <summary>
        /// Draws the frame of every group: a light-grey fill, a border, and a title strip with
        /// the group's name (ADR-0015).
        ///
        /// The frame is drawn <em>before</em> the cards, so it reads as a backdrop: the members
        /// are still the thing the player acts on, and the frame only says which blocks belong
        /// together. A group is not a card and deliberately does not look like one — it has no
        /// "+", no subtype line and no shadow, because it has no toolbar and nothing to fire.
        ///
        /// The frame has no card of its own to be selected by, so its title strip carries the
        /// selection and hover highlight. That is also its only click target: the interior
        /// belongs to the members.
        /// </summary>
        /// <summary>
        /// Draws the border of every grid in the construct (ADR-0032).
        ///
        /// <b>A thin line at every zoom, never a filled band.</b> The border exists to say "these
        /// cards are one ship part", and a line that grows with the zoom would compete with the
        /// cards it encloses — at 2x it would be twice as thick as a card's own border and read as
        /// a wall rather than a boundary. The thickness is therefore taken from
        /// <see cref="VgsScreenDraw.ClientWidth"/> in graph units, the same conversion the caret
        /// uses, so it lands on about one pixel at any resolution and any zoom.
        ///
        /// <b>Nothing is drawn for a construct with one grid.</b> A border around everything is
        /// not a boundary — it is a picture frame, and it would appear on every single-grid ship
        /// where it says nothing. The check is "does any node stand on a grid other than the one
        /// the editor was opened on", not "how many frames are there": a construct whose subgrid
        /// is entirely hidden still has one frame, and that frame means nothing.
        ///
        /// <b>Drawn first of everything on the canvas</b>, so it sits behind group frames, lines
        /// and cards: a grid contains groups, groups contain cards.
        /// </summary>
        private void DrawGridBorders(Vector2 clipTopLeft, Vector2 clipSize)
        {
            if (!_view.ShowGridBorders) return;

            GridFrames.Layout(_graph, _gridFrames);
            if (_gridFrames.Count == 0) return;

            bool multiGrid = false;
            for (int i = 0; i < _gridFrames.Count; i++)
            {
                if (_gridFrames[i].GridEntityId == _graph.GridEntityId) continue;
                multiGrid = true;
                break;
            }
            if (!multiGrid) return;

            for (int i = 0; i < _gridFrames.Count; i++)
            {
                GridFrame frame = _gridFrames[i];

                Vector2 screen = GraphToScreen(frame.TopLeft, clipTopLeft);
                Vector2 screenSize = frame.Size * _view.Zoom;

                // Cull the same way cards are culled: a grid scrolled off-screen costs one
                // comparison instead of a border. The predicate works in canvas coordinates —
                // comparing the absolute position against the canvas size cut the frame one
                // header height too early at the bottom (R-015).
                if (!CanvasRect.Overlaps(clipTopLeft, clipSize, screen, screenSize)) continue;

                // The editor's own grid is the quieter of the two: the player is looking at the
                // construct from that ship part, and a border at full strength around everything
                // would read as the outer edge of the picture rather than as one grid among
                // several.
                Color color = frame.GridEntityId == _graph.GridEntityId
                    ? ColGridFrameMain
                    : ColGridFrameSub;

                VgsScreenDraw.BorderPixels(screen, screenSize, clipTopLeft, clipSize,
                                           color, VgsConstants.GridBorderPixels);
            }

            // One line per session, naming what was actually drawn. Without it, a report of "a
            // border looks wrong" cannot be told apart from "a different border is wrong", and the
            // geometry can only be guessed at — which has cost three wrong fixes already. The line
            // carries the numbers the rasteriser was given, so the next report can be measured
            // instead of reasoned about.
            ReportGridGeometryOnce(clipTopLeft, clipSize);

            // Tabs in a second pass, after every border. A tab belongs to its frame, but a frame
            // drawn later would paint over it, and on a construct where a subgrid's box sits close
            // to the main grid's corner that is not hypothetical: the tab would be half covered by
            // a border belonging to another grid, which reads as a clipping defect.
            for (int i = 0; i < _gridFrames.Count; i++)
            {
                GridFrame frame = _gridFrames[i];

                Vector2 screen = GraphToScreen(frame.TopLeft, clipTopLeft);
                Vector2 screenSize = frame.Size * _view.Zoom;

                if (!CanvasRect.Overlaps(clipTopLeft, clipSize, screen, screenSize)) continue;

                Color color = frame.GridEntityId == _graph.GridEntityId
                    ? ColGridFrameMain
                    : ColGridFrameSub;

                DrawGridName(frame, screen, screenSize, clipTopLeft, clipSize, color);
            }
        }

        /// <summary>
        /// Logs the geometry of the grid borders once per session, in pixels.
        ///
        /// <b>Why this exists.</b> Four fixes in a row were reasoned from a screenshot rather than
        /// measured, and three were wrong. A report that says "the border has a spur" cannot
        /// distinguish a wrong corner from a wrong clip from a second box drawn at almost the same
        /// place; the numbers can. This prints the box <em>and the edge rectangles</em> the
        /// rasteriser was given, plus the tab, so the next report can be answered from data. There
        /// may be fewer than four of them: an edge that lies outside the canvas is dropped rather
        /// than moved onto the canvas boundary (R-015).
        ///
        /// One line per session, not per frame: it is a diagnostic, not telemetry.
        /// </summary>
        private void ReportGridGeometryOnce(Vector2 clipTopLeft, Vector2 clipSize)
        {
            if (_gridGeometryReported) return;
            if (_gridFrames.Count == 0) return;

            _gridGeometryReported = true;

            var report = new System.Text.StringBuilder();
            report.Append("grid borders: ").Append(_gridFrames.Count).Append(" frame(s), zoom ")
                  .Append(_view.Zoom.ToString("0.###"))
                  .Append(", client ").Append((int)VgsScreenDraw.ClientWidth).Append('x')
                  .Append((int)VgsScreenDraw.ClientHeight)
                  .Append(", scale ").Append(VgsScreenDraw.ApiScaleX.ToString("0.#####"))
                  .Append('/').Append(VgsScreenDraw.ApiScaleY.ToString("0.#####"))
                  .Append(", offset ").Append(VgsScreenDraw.ApiOffsetX.ToString("0.#####"))
                  .Append('/').Append(VgsScreenDraw.ApiOffsetY.ToString("0.#####"));

            // The canvas in pixels, so the report names the edges the rasteriser actually got:
            // an edge outside the clip is dropped rather than moved onto it (R-015).
            float clipLeft = GridFrames.CornerToApiPixels(VgsScreenDraw.ApiScaleX,
                                                          VgsScreenDraw.ApiOffsetX,
                                                          VgsScreenDraw.ClientWidth, clipTopLeft.X);
            float clipTop = GridFrames.CornerToApiPixels(VgsScreenDraw.ApiScaleY,
                                                         VgsScreenDraw.ApiOffsetY,
                                                         VgsScreenDraw.ClientHeight, clipTopLeft.Y);
            float clipRight = GridFrames.CornerToApiPixels(VgsScreenDraw.ApiScaleX,
                                                           VgsScreenDraw.ApiOffsetX,
                                                           VgsScreenDraw.ClientWidth,
                                                           clipTopLeft.X + clipSize.X);
            float clipBottom = GridFrames.CornerToApiPixels(VgsScreenDraw.ApiScaleY,
                                                            VgsScreenDraw.ApiOffsetY,
                                                            VgsScreenDraw.ClientHeight,
                                                            clipTopLeft.Y + clipSize.Y);

            for (int i = 0; i < _gridFrames.Count; i++)
            {
                GridFrame frame = _gridFrames[i];

                Vector2 screen = GraphToScreen(frame.TopLeft, clipTopLeft);
                Vector2 screenSize = frame.Size * _view.Zoom;

                float left = GridFrames.CornerToApiPixels(VgsScreenDraw.ApiScaleX,
                                                          VgsScreenDraw.ApiOffsetX,
                                                          VgsScreenDraw.ClientWidth, screen.X);
                float top = GridFrames.CornerToApiPixels(VgsScreenDraw.ApiScaleY,
                                                         VgsScreenDraw.ApiOffsetY,
                                                         VgsScreenDraw.ClientHeight, screen.Y);
                float right = GridFrames.CornerToApiPixels(VgsScreenDraw.ApiScaleX,
                                                           VgsScreenDraw.ApiOffsetX,
                                                           VgsScreenDraw.ClientWidth,
                                                           screen.X + screenSize.X);
                float bottom = GridFrames.CornerToApiPixels(VgsScreenDraw.ApiScaleY,
                                                            VgsScreenDraw.ApiOffsetY,
                                                            VgsScreenDraw.ClientHeight,
                                                            screen.Y + screenSize.Y);

                report.Append(" | grid ").Append(frame.GridEntityId)
                      .Append(frame.GridEntityId == _graph.GridEntityId ? "(main)" : "(sub)")
                      .Append(" box ").Append((int)left).Append(',').Append((int)top)
                      .Append("..").Append((int)right).Append(',').Append((int)bottom);

                // The rectangles actually handed to the sprite, after the clip. If two of them
                // disagree by a pixel at a shared corner, that disagreement is visible here.
                var edges = new List<Vector4>(4);
                GridFrames.AppendBorderEdgesClipped(edges, left, top, right, bottom,
                                                    clipLeft, clipTop, clipRight, clipBottom,
                                                    VgsConstants.GridBorderPixels);
                for (int e = 0; e < edges.Count; e++)
                {
                    Vector4 edge = edges[e];
                    report.Append(" e").Append(e).Append('[')
                          .Append((int)edge.X).Append(',').Append((int)edge.Y).Append("..")
                          .Append((int)edge.Z).Append(',').Append((int)edge.W).Append(']');
                }
            }

            VgsLog.Info(report.ToString());
        }

        private static string FormatPoint(Vector2 point)
        {
            return point.X.ToString("0.####") + "," + point.Y.ToString("0.####");
        }

        /// <summary>Draws a grid's name as a tab sitting on the frame's top-left corner.
        ///
        /// <b>On the corner, not inside the frame.</b> A label inside would have to clear the
        /// cards, and the frame's padding is only as large as it needs to be to keep the border
        /// off them — there is no room for text there, which is why the label used to look
        /// detached from its border. A tab whose bottom edge lies on the top edge reads as part of
        /// the frame at any zoom, the way a filename tab reads as part of a folder.
        ///
        /// The tab is filled with the canvas colour so the border behind it does not show through
        /// the glyphs, and outlined in the grid's own colour so the two are visibly one object.
        ///
        /// A name that cannot be read is not drawn: the border still says the cards belong
        /// together, which is the part that matters.
        /// </summary>
        private void DrawGridName(GridFrame frame, Vector2 screen, Vector2 screenSize,
                                  Vector2 clipTopLeft, Vector2 clipSize, Color color)
        {
            string name = GridNameFor(frame.GridEntityId);
            if (string.IsNullOrEmpty(name)) return;

            const float scale = VgsConstants.GridTitleScale;
            Vector2 textSize = VgsScreenDraw.Measure(name, scale);

            float padX = VgsConstants.GridTabPaddingX;
            float padY = VgsConstants.GridTabPaddingY;

            float tabWidth = textSize.X + padX * 2f;
            float tabHeight = textSize.Y + padY * 2f;
            if (tabWidth <= 0f || tabHeight <= 0f) return;

            // The tab's bottom edge sits on the frame's top edge; its left edge aligns with the
            // frame's left edge. Rounded to whole pixels so the tab's outline and the border it
            // rests on share a row instead of overlapping by a fraction.
            float tabLeft = screen.X;
            float tabTop = screen.Y - tabHeight;

            Vector2 tabTopLeft = new Vector2(tabLeft, tabTop);
            Vector2 tabSize = new Vector2(tabWidth, tabHeight);

            // Cull with the frame: a tab alone, with its frame off-screen, is noise. Canvas
            // coordinates, like every other cull (R-015).
            if (!CanvasRect.Overlaps(clipTopLeft, clipSize, tabTopLeft, tabSize)) return;

            // Filled with the canvas colour rather than left transparent: the frame's own top edge
            // runs along the tab's bottom, and a translucent tab would show it through the text.
            //
            // Both the fill and the outline go through the pixel-snapped primitives, from the same
            // corners. A fill at a fractional position beside a border on whole pixels leaves a
            // fraction of the fill showing past the border — which on a dark canvas reads as a
            // one-pixel spur at a corner. This is the same defect ADR-0024 §5 records for the
            // search field, and the same answer: one set of edges, not two.
            VgsScreenDraw.FillPixels(tabTopLeft, tabSize, ColCanvas);
            VgsScreenDraw.BorderPixels(tabTopLeft, tabSize, clipTopLeft, clipSize, color,
                                       VgsConstants.GridBorderPixels);

            float textLeft = tabLeft + padX;
            float textTop = tabTop + padY;
            float textWidth = tabWidth - padX * 2f;
            if (textWidth <= 0f) return;

            VgsScreenDraw.Text(name, new Vector2(textLeft, textTop), scale, color, textWidth);
        }

        /// <summary>
        /// Display name of a grid, cached per refresh. Empty when it cannot be read.
        ///
        /// Cached because the draw path runs every frame and reading a grid's display name is a
        /// live world read (ADR-0003 forbids those while drawing). The cache is rebuilt by
        /// <see cref="RefreshGridNames"/> on the structural pass, where world reads belong.
        /// </summary>
        private string GridNameFor(long gridEntityId)
        {
            if (_gridNames == null) return string.Empty;

            string name;
            return _gridNames.TryGetValue(gridEntityId, out name) ? name : string.Empty;
        }

        private void DrawGroupFrames(Vector2 clipTopLeft, Vector2 clipSize)
        {
            IList<Node> groups = _graph.Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                Node group = groups[i];

                Vector2 topLeft, size;
                if (!PortGeometry.TryBodyRect(group, out topLeft, out size)) continue;

                Vector2 screen = GraphToScreen(topLeft, clipTopLeft);
                Vector2 screenSize = size * _view.Zoom;

                // Cull against the canvas the same way cards are culled, so a group scrolled
                // off-screen costs one comparison instead of a rectangle and a border.
                if (!CanvasRect.Overlaps(clipTopLeft, clipSize, screen, screenSize)) continue;

                bool selected = ReferenceEquals(group, _selected);
                bool hovered = ReferenceEquals(group, _hoverNode);

                // A group follows its members through the search: it is a backdrop for them, so
                // it is dimmed only when none of its drawn members matches (ADR-0023).
                float alpha = AlphaOf(group);

                VgsScreenDraw.RectClipped(screen, screenSize, clipTopLeft, clipSize,
                                          Fade(ColGroupFill, alpha));

                Vector2 titleTopLeft, titleSize;
                if (PortGeometry.TryGroupTitleRect(group, out titleTopLeft, out titleSize))
                {
                    VgsScreenDraw.RectClipped(GraphToScreen(titleTopLeft, clipTopLeft),
                                              titleSize * _view.Zoom, clipTopLeft, clipSize,
                                              Fade(ColGroupTitle, alpha));
                }

                Color border = selected ? ColGroupSelected : (hovered ? ColNodeHover : ColGroupBorder);

                // The group frame goes through the same pixel-snapped border as the grid frames.
                //
                // <b>Why this is not cosmetic.</b> The old call used `BorderClipped` with a
                // `Hairline` thickness — a fraction of the client *height*, in layout units, at
                // unsnapped coordinates. Two consequences, and the second is the visible one:
                //
                //  * the two axes do not share a scale (ADR-0009), so a hairline is 1 px tall and
                //    1.78 px wide on a 16:9 window;
                //  * the vertical bars and the horizontal ones are separate quads on a shared
                //    nominal coordinate, rasterised independently, so one can reach a pixel past
                //    the other — a stub sticking out of a corner. `Border`'s own documentation
                //    records exactly this defect ("two little feet sticking out under a field's
                //    bottom edge"), and it is the same one reported here at every zoom.
                //
                // Using one construction for every frame in the editor means a corner cannot be
                // assembled from two different rounding rules.
                VgsScreenDraw.BorderPixels(screen, screenSize, clipTopLeft, clipSize,
                                           Fade(border, alpha),
                                           selected ? VgsConstants.GridBorderPixels * 2f
                                                    : VgsConstants.GridBorderPixels);

                if (titleSize.Y > 0f)
                {
                    VgsScreenDraw.Text(group.DisplayName,
                                       GraphToScreen(titleTopLeft, clipTopLeft)
                                           + new Vector2(screenSize.X * 0.012f, titleSize.Y * _view.Zoom * 0.18f),
                                       VgsConstants.GroupTitleScale * CardTextScale,
                                       Fade(ColText, alpha), screenSize.X * 0.976f);
                }
            }
        }

        /// <summary>
        /// Draws the port bars of every group.
        ///
        /// A group has incoming ports only — what may be done to its members — so this is a
        /// short pass and it never has an output stack to skip past. It runs after the cards
        /// because a group's bars hang under its frame, which is under its members, so nothing
        /// is covered; and after the lines, because a line ends on the bar's dot and has to be
        /// under it.
        /// </summary>
        private void DrawGroupPorts(Vector2 clipTopLeft, Vector2 clipSize)
        {
            IList<Node> groups = _graph.Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                Node group = groups[i];
                bool selected = ReferenceEquals(group, _selected);

                // One alpha per group: its bars belong to the frame and follow it (ADR-0023).
                // The border is faded here, not inside DrawPortBar, for the same reason a block's
                // is faded in DrawNode: a bar is drawn in its node's own frame colour, so a ghost
                // node whose bars kept a full-opacity frame would be the brightest thing on the
                // canvas — exactly what the search is supposed to de-emphasise.
                float alpha = AlphaOf(group);
                Color border = Fade(selected ? ColGroupSelected : ColGroupBorder, alpha);

                for (int p = 0; p < group.Actions.Count; p++)
                {
                    DrawPortBar(group, group.Actions[p], border, alpha, clipTopLeft, clipSize);
                }
            }
        }

        /// <summary>
        /// Draws the card's button row: one icon per button, in the order the row is defined in
        /// <see cref="PortGeometry"/> (ADR-0020, ADR-0021).
        ///
        /// <b>One shape for every button.</b> All of them are bare icons of one size — no disc,
        /// no square, no letter. Which icon it is says what the button does: the square with an
        /// arrow leaving it jumps to the block's terminal, a bell starts a watch, a filled
        /// lightbulb binds the "yes" list, an outlined one the "no" list, and a square-plus the
        /// single list of a block with one toolbar.
        ///
        /// <b>One colour too.</b> Every button of the row is near-white at rest and warms to
        /// <see cref="ColPlusFillHot"/> under the pointer — the same two states the "+" has
        /// always had. The watch button used to be drawn in the watch yellow, on the theory that
        /// colour should separate observation from action; in the game that made it the one
        /// permanently yellow control on the canvas and it read as a state ("watching") rather
        /// than as a button, at every zoom and whether or not the controller was watching
        /// anything. Colour now marks what *happened* — the watch bars and the dashed line are
        /// yellow — while a control's colour marks what the pointer is doing to it.
        ///
        /// When a texture is missing the button falls back to its old glyph — "&gt;", "#", "T",
        /// "F" or "+" — in the same colour, so a missing atlas costs the shape of the mark and
        /// never the control (ADR-0019 §6).
        ///
        /// <b>One pass over the row.</b> The kinds are collected into
        /// <see cref="_cardButtonRow"/> and each button's rectangle is computed from its position
        /// in that row, so this runs once per card instead of searching the row once per button.
        /// The buffer is a field because <see cref="OnDraw"/> runs every frame for every visible
        /// card and allocating here would break R010.
        /// </summary>
        private void DrawCardButtons(Node node, float alpha, Vector2 clipTopLeft, Vector2 clipSize)
        {
            int count = PortGeometry.GetCardButtons(node, _cardButtonRow);
            float aspect = VgsScreenDraw.ClientAspect;

            for (int i = 0; i < count; i++)
            {
                Vector2 topLeft, size;
                PortGeometry.CardButtonRectAt(node, i, count, aspect, out topLeft, out size);
                DrawCardButton(node, _cardButtonRow[i], topLeft + size * 0.5f, alpha,
                               clipTopLeft, clipSize);
            }
        }

        /// <summary>Draws one button of a card's row: its icon, or its glyph when the icon is unavailable.</summary>
        private void DrawCardButton(Node node, CardButtonKind kind, Vector2 center, float alpha,
                                    Vector2 clipTopLeft, Vector2 clipSize)
        {
            Vector2 screenCenter = GraphToScreen(center, clipTopLeft);

            bool hot = ReferenceEquals(node, _hoverButton) && _hoverButtonKind == kind;
            Color color = Fade(hot ? ColPlusFillHot : ColPlusFill, alpha);

            float diameter = VgsConstants.CardButtonDiameter * _view.Zoom;
            if (VgsScreenDraw.Icon(IconOf(kind), screenCenter,
                                   diameter * VgsConstants.CardButtonIconRatio, color,
                                   clipTopLeft, clipSize))
            {
                return;
            }

            // No texture: the glyph the button carried before it became an icon, drawn in the
            // same colour so the control is still there and still says the same thing.
            VgsScreenDraw.TextCentered(GlyphOf(kind), screenCenter,
                                       VgsConstants.CardButtonGlyphScale * CardTextScale, color);
        }

        /// <summary>
        /// The icon each button draws. The mapping lives here rather than in
        /// <see cref="PortGeometry"/> because it is a presentation decision: geometry says where
        /// a button is, this says what it looks like. Which icon is which is recorded in
        /// `docs/07-icon-catalog.md`, and `tools/check-icon-catalog.ps1` fails when this table
        /// and the catalogue disagree.
        /// </summary>
        private static VgsIcon IconOf(CardButtonKind kind)
        {
            switch (kind)
            {
                case CardButtonKind.Jump: return VgsIcon.ShareFromSquare;
                case CardButtonKind.Watch: return VgsIcon.Bell;
                case CardButtonKind.BindYes: return VgsIcon.LightbulbSolid;
                case CardButtonKind.BindNo: return VgsIcon.LightbulbRegular;
                default: return VgsIcon.SquarePlus;
            }
        }

        /// <summary>
        /// The glyph a button falls back to when its texture is unavailable: the mark it carried
        /// before it became an icon.
        /// </summary>
        private static string GlyphOf(CardButtonKind kind)
        {
            switch (kind)
            {
                case CardButtonKind.Jump: return VgsText.JumpGlyph;
                case CardButtonKind.Watch: return VgsText.WatchGlyph;
                case CardButtonKind.BindYes: return VgsText.TrueButtonGlyph;
                case CardButtonKind.BindNo: return VgsText.FalseButtonGlyph;
                default: return VgsText.SingleButtonGlyph;
            }
        }

        /// <summary>
        /// Zoom factor for the card's text (name, subtype, the status line).
        ///
        /// Clamped on both ends and, crucially, <b>below</b> the old floor of 0.75: the card is
        /// now half as tall as it was, and two lines of text at the old floor overflow it once
        /// the user zooms out to <see cref="VgsConstants.MinZoom"/>. Text that is unreadable but
        /// inside its box is a better failure than text that is unreadable and spills over its
        /// neighbours.
        ///
        /// Port labels deliberately do <b>not</b> use this clamp: they are multiplied by the
        /// zoom directly (<see cref="VgsConstants.PortLabelScale"/>), because a port bar shrinks
        /// with the canvas and its label has to shrink with it instead of overflowing a bar that
        /// kept shrinking (ADR-0014).
        /// </summary>
        private float CardTextScale
        {
            get
            {
                if (_view.Zoom < 0.55f) return 0.55f;
                if (_view.Zoom > 1.6f) return 1.6f;
                return _view.Zoom;
            }
        }

        /// <summary>
        /// Draws the port bars of one card: outputs first (flush with the card), then inputs
        /// below them, each with its dot, its label and its remove button.
        ///
        /// <paramref name="border"/> is the card's own frame colour, passed in rather than
        /// re-derived: a port belongs to the block, so it must follow the card's state too —
        /// a selected block whose ports stayed grey would break the column into two figures at
        /// exactly the moment the user is looking at it (ADR-0014).
        /// </summary>
        private void DrawPortBars(Node node, Color border, float alpha, Vector2 clipTopLeft,
                                  Vector2 clipSize)
        {
            for (int i = 0; i < node.Events.Count; i++)
                DrawPortBar(node, node.Events[i], border, alpha, clipTopLeft, clipSize);
            for (int i = 0; i < node.Actions.Count; i++)
                DrawPortBar(node, node.Actions[i], border, alpha, clipTopLeft, clipSize);

            // The watch stack follows the input stack in the same column (ADR-0017).
            DrawWatchBars(node, border, alpha, clipTopLeft, clipSize);
        }

        /// <summary>
        /// Draws one port bar: its background, its frame, its label, the connection dot and the
        /// remove button.
        ///
        /// <b>The frame is the card's frame.</b> A port belongs to the block, so it is drawn in
        /// the card's own border colour — including the selected and hovered variants — and only
        /// its background differs. That is what makes the column read as one block with two
        /// backgrounds rather than as a card with three separate boxes hanging off it (ADR-0014).
        /// The dot keeps its own colour, because it is what tells an input from an output at a
        /// glance and it matches the line that leaves it.
        ///
        /// <b>The label is centred on the bar and scales with the zoom.</b> Both controls sit at
        /// the bar's ends, so the label's span is the same for an input and an output; it is
        /// positioned by its measured line height so that its centre line is the bar's centre
        /// line, not its top edge.
        /// </summary>
        private void DrawPortBar(Node node, Port port, Color border, float alpha, Vector2 clipTopLeft,
                                 Vector2 clipSize)
        {
            if (port.Slot < 0) return;

            Vector2 topLeft, size;
            if (!PortGeometry.TryPortBarRect(node, port, out topLeft, out size)) return;

            Vector2 screenTopLeft = GraphToScreen(topLeft, clipTopLeft);
            Vector2 screenSize = size * _view.Zoom;

            bool output = port.Direction == PortDirection.Output;

            // The port follows the card through the search: a bar that stayed bright under a
            // ghost card would be the brightest thing on the canvas and read as a match.
            Color fill = Fade(output ? ColPortOutFill : ColPortInFill, alpha);
            Color accent = output ? ColPortOut : ColPortIn;
            Color accentFaded = Fade(accent, alpha);

            VgsScreenDraw.RectClipped(screenTopLeft, screenSize, clipTopLeft, clipSize, fill);
            VgsScreenDraw.BorderClipped(screenTopLeft, screenSize, clipTopLeft, clipSize, border,
                                        Hairline);

            // The label is clipped to the span between the two controls, so a long action name
            // can neither spill over the next card nor run under a disc.
            Vector2 labelTopLeft, labelSize;
            if (PortGeometry.TryPortLabelRect(node, port, VgsScreenDraw.ClientAspect,
                                              out labelTopLeft, out labelSize))
            {
                Vector2 labelScreen = GraphToScreen(labelTopLeft, clipTopLeft);
                float textHeight = PortLabelHeight;
                if (textHeight <= 0f) textHeight = screenSize.Y * VgsConstants.PortLabelFallbackHeightRatio;

                VgsScreenDraw.Text(port.DisplayName,
                                   new Vector2(labelScreen.X,
                                               PortGeometry.CentredTextTop(labelScreen.Y,
                                                                           screenSize.Y, textHeight)),
                                   VgsConstants.PortLabelScale * _view.Zoom,
                                   Fade(ColText, alpha), labelSize.X * _view.Zoom);
            }

            float radius = VgsConstants.PortControlRadius * _view.Zoom;

            // The dot: where the line attaches, one control-width and one four-pixel gap inside
            // the edge the line uses — right for an output, left for an input. Same shape both
            // ways on purpose; the side it sits on is the property the router relies on.
            Vector2 dotCenter;
            if (PortGeometry.TryPortDot(node, port, VgsScreenDraw.ClientAspect, out dotCenter))
            {
                DrawPortDot(GraphToScreen(dotCenter, clipTopLeft), radius, accentFaded, clipTopLeft,
                            clipSize);
            }

            // The remove button: the dot's mirror image, on the other side of the same bar.
            Vector2 removeCenter;
            if (PortGeometry.TryPortRemoveButton(node, port, VgsScreenDraw.ClientAspect, out removeCenter))
            {
                bool hot = ReferenceEquals(node, _hoverRemoveNode)
                           && ReferenceEquals(port, _hoverRemovePort);

                // Accent, not the bar's fill: the mark is now bare, so its colour has to carry
                // the contrast instead of the white disc that used to sit behind it. The alpha is
                // passed separately rather than pre-applied, because the hot colour is chosen
                // inside — a caller-side fade would leave the hover state at full opacity on a
                // ghost bar.
                DrawPortRemoveButton(GraphToScreen(removeCenter, clipTopLeft), accent, alpha, hot,
                                     clipTopLeft, clipSize);
            }
        }

        /// <summary>
        /// Draws a port's connection dot: the Font Awesome <c>circle-dot</c> icon at the size the
        /// dot has always had, or a filled disc when the icon cannot be drawn (ADR-0019).
        ///
        /// The shape changes on purpose — a ring instead of a disc — but nothing else does: the
        /// diameter, the colour, the position and the hit-test all come from the same constants
        /// they came from before, because the icon *is* the dot rather than a mark on it.
        ///
        /// The disc is a fallback for a missing texture only. It used to also cover small sizes;
        /// that is gone, because a dot that is a ring at one zoom and a disc at the next reads as
        /// a fault (ADR-0019 §6).
        ///
        /// A watch port uses the same call with its own colour. Two different shapes for the same
        /// meaning on one card would be a language nobody reads.
        /// </summary>
        private void DrawPortDot(Vector2 screenCenter, float radius, Color color,
                                 Vector2 clipTopLeft, Vector2 clipSize)
        {
            if (VgsScreenDraw.Icon(VgsIcon.CircleDot, screenCenter, radius * 2f, color,
                                   clipTopLeft, clipSize))
            {
                return;
            }

            VgsScreenDraw.Disc(screenCenter, radius, clipTopLeft, clipSize, color);
        }

        /// <summary>
        /// Draws a port's remove button: the bare <c>xmark</c> icon, in the port's accent colour
        /// (ADR-0019).
        ///
        /// It used to be an inverted white disc with a dark mark on it (ADR-0014), and that was
        /// right while the mark was a font glyph that had no shape of its own. Now that the dot
        /// beside it is a bare ring, the disc is the odd one out: the pair reads as "ring =
        /// connection, cross = removal" only if neither has a filled backdrop. The colour carries
        /// the affordance instead — accent for the mark, the bar's own fill behind it — and hover
        /// brightens the mark, which is the same feedback the dot gets.
        ///
        /// <paramref name="accent"/> is passed <em>unfaded</em>, together with
        /// <paramref name="alpha"/>, because the hover colour is chosen here: fading at the call
        /// site would fade the resting mark and leave the hot one at full brightness, so a ghost
        /// bar's "x" would light up the moment the pointer touched it. The card's buttons solve
        /// the same problem the same way (<see cref="DrawCardButton"/>).
        ///
        /// The hit target is unchanged: it comes from the radius in `PortGeometry`, not from what
        /// is painted.
        /// </summary>
        private void DrawPortRemoveButton(Vector2 screenCenter, Color accent, float alpha, bool hot,
                                          Vector2 clipTopLeft, Vector2 clipSize)
        {
            float radius = VgsConstants.PortControlRadius * _view.Zoom;
            float diameter = radius * 2f;

            Color mark = Fade(hot ? ColPlusFillHot : accent, alpha);

            if (VgsScreenDraw.Icon(VgsIcon.Xmark, screenCenter,
                                   diameter * VgsConstants.PortRemoveIconRatio, mark,
                                   clipTopLeft, clipSize))
            {
                return;
            }

            // Fallback: the font's "x", in the same colour the icon would have used.
            VgsScreenDraw.TextCentered(VgsText.PortRemoveGlyph, screenCenter,
                                       VgsConstants.PortRemoveGlyphScale * _view.Zoom, mark);
        }

        /// <summary>
        /// Height of one line of port-label text at the current zoom, in client-area units.
        ///
        /// Measured once per zoom level rather than per label. The height of a line is a
        /// property of the font and the scale, not of the string, while
        /// <see cref="DrawPortBar"/> runs for every drawn port of every visible card on every
        /// frame — measuring there would call into the font metrics thousands of times a second.
        /// The measurement is what puts the label's *centre* on the bar's centre line;
        /// positioning text by its top edge would leave it sitting low by half a line.
        ///
        /// The cache key is the zoom alone. That is sound because the editor is a modal,
        /// full-screen screen that pauses the game, so the resolution cannot change underneath
        /// it; a font that refused to measure caches a zero, which the caller replaces with
        /// <see cref="VgsConstants.PortLabelFallbackHeightRatio"/> instead of retrying every
        /// frame. If the screen ever stops being modal, the key has to gain the client height.
        /// </summary>
        private float PortLabelHeight
        {
            get
            {
                if (_portLabelHeightZoom == _view.Zoom) return _portLabelHeight;

                _portLabelHeightZoom = _view.Zoom;
                _portLabelHeight =
                    VgsScreenDraw.Measure(LabelProbeText, VgsConstants.PortLabelScale * _view.Zoom).Y;
                return _portLabelHeight;
            }
        }

        /// <summary>
        /// Text measured to find a line's height. Both cases are needed so the measurement
        /// covers the font's full line box: "A" gives the ascender, "g" the descender.
        /// </summary>
        private const string LabelProbeText = "Ag";

        /// <summary>
        /// Draws the action menu: the target's terminal actions, one row each, in the order
        /// the terminal shows them.
        /// </summary>
        private void DrawActionMenu()
        {
            Vector2 topLeft, size;
            GetMenuRect(out topLeft, out size);

            // Panel rather than Rect + Border: the fill is inset under the frame, so it cannot
            // round a row past the menu's bottom edge (ADR-0024 §5).
            VgsScreenDraw.Panel(topLeft, size, ColMenu, ColMenuBorder, Hairline);

            // The title shrinks to make room for the "rows 1-9 / 15" counter rather than being
            // clipped by it: the counter is the only sign that the list continues past the
            // visible rows, so it must never be the text that loses the fight for space.
            float titleWidth = size.X - VgsConstants.PortLabelPadding * 3.2f;
            Vector2 titleTopLeft = topLeft + new Vector2(VgsConstants.PortLabelPadding * 1.6f,
                                                         VgsConstants.ActionMenuTitleHeight * 0.22f);

            if (_menuCountLabel != null)
            {
                float countScale = VgsConstants.NodeSubtitleScale * 0.85f;
                Vector2 countSize = VgsScreenDraw.Measure(_menuCountLabel, countScale);

                VgsScreenDraw.TextRight(_menuCountLabel,
                                        new Vector2(topLeft.X + size.X - VgsConstants.PortLabelPadding * 1.6f,
                                                    titleTopLeft.Y),
                                        countScale, ColTextDim);

                titleWidth -= countSize.X + VgsConstants.PortLabelPadding;
                if (titleWidth < 0f) titleWidth = 0f;
            }

            VgsScreenDraw.Text(_menuTitle, titleTopLeft,
                               VgsConstants.NodeTitleScale * 0.85f, ColText, titleWidth);

            if (_menuRows.Count == 0)
            {
                Vector2 rowTopLeft, rowSize;
                GetMenuRowRect(0, out rowTopLeft, out rowSize);
                VgsScreenDraw.Text(VgsText.ActionMenuEmpty,
                                   rowTopLeft + new Vector2(VgsConstants.PortLabelPadding * 1.6f,
                                                            rowSize.Y * 0.22f),
                                   VgsConstants.NodeSubtitleScale * 0.9f, ColTextDim,
                                   rowSize.X - VgsConstants.PortLabelPadding * 3.2f);
                return;
            }

            // Rows keep clear of the scroll indicator when there is one, so a long caption is
            // clipped by the menu edge rather than running under the thumb.
            float rowTextWidth = size.X - VgsConstants.PortLabelPadding * 3.2f;
            if (_menuCountLabel != null) rowTextWidth -= MenuScrollbarWidth + MenuScrollbarInset;

            for (int i = 0; i < VisibleMenuRows; i++)
            {
                int index = i + _menuScroll;
                if (index >= _menuRows.Count) break;

                Vector2 rowTopLeft, rowSize;
                GetMenuRowRect(i, out rowTopLeft, out rowSize);

                if (index == _menuHoverRow)
                {
                    VgsScreenDraw.Rect(rowTopLeft, rowSize, ColMenuRowHot);
                }

                VgsScreenDraw.Text(_menuLabels[index],
                                   rowTopLeft + new Vector2(VgsConstants.PortLabelPadding * 1.6f,
                                                            rowSize.Y * 0.22f),
                                   VgsConstants.NodeSubtitleScale * 0.95f, ColText,
                                   rowTextWidth);
            }

            // Track and thumb last, so the thumb is not covered by a hovered row.
            if (_menuCountLabel == null) return;

            Vector2 trackTopLeft, trackSize, thumbTopLeft, thumbSize;
            GetMenuScrollbarRects(out trackTopLeft, out trackSize, out thumbTopLeft, out thumbSize);

            VgsScreenDraw.Rect(trackTopLeft, trackSize, ColMenuRowHot);
            VgsScreenDraw.Rect(thumbTopLeft, thumbSize, ColMenuBorder);
        }

        /// <summary>
        /// Draws the bottom bar: the modes button, the block search field, and Close (ADR-0023).
        ///
        /// The bar no longer carries the statistics — they moved to the header — and no longer
        /// carries the one-line help text, which is gone. What is left is the chrome the player
        /// actually uses while looking at the schema. The failure message keeps its line above the
        /// controls: it is the one thing that must not be pushed off the bar by anything.
        /// </summary>
        private void DrawBottomBar()
        {
            Vector2 barTopLeft, barSize;
            GetBottomBarRect(out barTopLeft, out barSize);

            VgsScreenDraw.Rect(barTopLeft, barSize, ColBottomBar);
            VgsScreenDraw.Rect(barTopLeft, new Vector2(1f, Hairline), ColBarSeparator);

            DrawModesButton();
            DrawSearchField();

            // The hint line has two jobs and one slot: it either explains the gesture under the
            // pointer, or it says why the last click did not produce a connection. The failure
            // wins, because the user is looking at a line that did not appear.
            Vector2 closeTopLeft, closeSize;
            GetCloseButtonRect(out closeTopLeft, out closeSize);
            float hintWidth = closeTopLeft.X - VgsConstants.BarLeftMargin - VgsConstants.CardButtonTextGap;

            string warning = ActiveWarning;
            string hint = warning ?? BottomBarHint;
            Color hintColor = warning != null ? ColWarning : ColTextDim;

            // The hint occupies the strip above the control row, vertically centred in it, so it
            // cannot collide with the controls whatever the font's line height is. Clamped to the
            // bar's own top edge so a very tall line still starts inside the bar.
            float hintHeight = HintLineHeight;
            float stripTop = barTopLeft.Y;
            float stripBottom = BarControlTop;
            float hintTop = stripTop + ((stripBottom - stripTop) - hintHeight) * 0.5f;
            if (hintTop < stripTop) hintTop = stripTop;

            if (hint.Length > 0)
            {
                VgsScreenDraw.Text(hint, new Vector2(VgsConstants.BarLeftMargin, hintTop),
                                   VgsConstants.HintTextScale, hintColor, hintWidth);
            }

            DrawCloseButton();
        }

        /// <summary>
        /// Draws the modes button: a bare <c>sliders</c> icon in the same light colour as the
        /// card's buttons, warming under the pointer, with the glyph "=" when the atlas is
        /// unavailable (ADR-0019 §6, ADR-0023).
        /// </summary>
        private void DrawModesButton()
        {
            Vector2 topLeft, size;
            GetModesButtonRect(out topLeft, out size);

            Color color = _modesHot || _modesMenuOpen ? ColPlusFillHot : ColPlusFill;
            Vector2 center = topLeft + size * 0.5f;

            // The icon is sized by the control's height less a hair of padding, so it never
            // touches the bar's edges at any resolution.
            float ink = size.Y * 0.72f;
            if (VgsScreenDraw.Icon(VgsIcon.Sliders, center, ink, color, Vector2.Zero,
                                   new Vector2(1f, 1f)))
            {
                return;
            }

            VgsScreenDraw.TextCentered(VgsText.ModesGlyph, center, VgsConstants.ModesMenuLabelScale,
                                       color);
        }

        /// <summary>
        /// Draws the search field: a bordered box, the query, the selection wash, the blinking
        /// caret, and the clear button at the right end while there is something to clear
        /// (ADR-0023, ADR-0024).
        ///
        /// <b>Layout before painting.</b> Everything positional — where the caret is, where the
        /// selection edges are, how far the text is scrolled — comes from
        /// <see cref="EnsureFieldLayout"/>, which recomputes only when something moved. Painting
        /// is then pure reads, so the per-frame cost of an idle field is a rectangle, a string and
        /// two comparisons.
        /// </summary>
        private void DrawSearchField()
        {
            Vector2 topLeft, size;
            GetSearchFieldRect(out topLeft, out size);

            bool empty = !SearchHasText;

            // The fill is inset by the frame's thickness: a fill and a frame on the same nominal
            // rectangle are two quads rasterised independently, and at a fractional position the
            // fill could cover a row past the frame — the stray line under the field that this
            // replaces (see VgsScreenDraw.Panel).
            VgsScreenDraw.Panel(topLeft, size, ColNode,
                                _searchField.Hot ? ColNodeHover : ColNodeBorder, Hairline);

            float clearReserve = 0f;
            Vector2 clearTopLeft, clearSize;
            if (!empty && TrySearchClearRect(out clearTopLeft, out clearSize))
            {
                clearReserve = size.X - (clearTopLeft.X - topLeft.X);
            }

            float textAreaWidth = size.X - VgsConstants.SearchFieldPadding * 2f - clearReserve;
            if (textAreaWidth <= 0f) return;

            _searchField.EnsureLayout(textAreaWidth);

            float textLeft = topLeft.X + VgsConstants.SearchFieldPadding;
            float textRight = textLeft + textAreaWidth;
            float textTop = topLeft.Y + (size.Y - SearchLineHeight) * 0.5f;
            if (textTop < topLeft.Y) textTop = topLeft.Y;

            // ---- selection wash ----
            //
            // Drawn before the text so the glyphs stay readable on top of it. Both edges are
            // clamped into the text area, which is what keeps the wash from painting over the
            // field's frame when the selection is scrolled partly out of view.
            if (_searchField.HasSelection)
            {
                float selectionLeft = textLeft + _searchField.SelectionStartX - _searchField.ScrollX;
                float selectionRight = textLeft + _searchField.SelectionEndX - _searchField.ScrollX;

                if (selectionLeft < textLeft) selectionLeft = textLeft;
                if (selectionRight > textRight) selectionRight = textRight;

                if (selectionRight > selectionLeft)
                {
                    Color wash = new Color(ColNodeHover, VgsConstants.SelectionFillOpacity);
                    VgsScreenDraw.Rect(new Vector2(selectionLeft, textTop),
                                       new Vector2(selectionRight - selectionLeft, SearchLineHeight),
                                       wash);
                }
            }

            // ---- text or placeholder ----
            if (empty)
            {
                VgsScreenDraw.Text(VgsText.SearchPlaceholder, new Vector2(textLeft, textTop),
                                   VgsConstants.SearchTextScale, ColTextDim, textAreaWidth);
            }
            else if (_searchField.DisplayText != null && _searchField.DisplayText.Length > 0)
            {
                // The painted string starts at the first character that is not scrolled off the
                // left edge; its origin is that character's own x, minus the scroll. Both numbers
                // come from the cached layout — measuring here would call the font metrics every
                // frame for a position that only moves when the player types (R010).
                float displayLeft = textLeft + _searchField.FirstVisibleX - _searchField.ScrollX;

                float displayWidth = textRight - displayLeft;
                if (displayWidth > 0f)
                {
                    VgsScreenDraw.Text(_searchField.DisplayText, new Vector2(displayLeft, textTop),
                                       VgsConstants.SearchTextScale, ColText, displayWidth);
                }
            }

            // ---- caret ----
            //
            // Drawn last so nothing covers it, and only on the lit half of the blink cycle. The
            // period restarts on every edit (inside SearchField), so a keystroke never lands on a
            // dark half and looks like it did nothing.
            if (_searchField.CaretVisible(_screenFrame))
            {
                float caretX = textLeft + _searchField.CaretX - _searchField.ScrollX;
                if (caretX < textLeft) caretX = textLeft;
                if (caretX > textRight) caretX = textRight;

                VgsScreenDraw.Rect(new Vector2(caretX - CaretThickness * 0.5f, textTop),
                                   new Vector2(CaretThickness, SearchLineHeight), ColText);
            }

            if (empty) return;
            if (!TrySearchClearRect(out clearTopLeft, out clearSize)) return;

            // The mark is drawn from the same rectangle the input pass hit-tests, so the visible
            // "x" and its click target cannot disagree.
            Vector2 clearCenter = clearTopLeft + clearSize * 0.5f;
            float mark = clearSize.Y * VgsConstants.PortRemoveIconRatio;

            if (VgsScreenDraw.Icon(VgsIcon.Xmark, clearCenter, mark,
                                   _searchField.ClearHot ? ColPlusFillHot : ColTextDim,
                                   Vector2.Zero, new Vector2(1f, 1f)))
            {
                return;
            }

            VgsScreenDraw.TextCentered(VgsText.PortRemoveGlyph, clearCenter,
                                       VgsConstants.PortRemoveGlyphScale, ColTextDim);
        }

        /// <summary>
        /// Width of the caret in the layout's units, from its pixel size at the reference height.
        ///
        /// <b>Divided by the client WIDTH, not by the reference height.</b> The two axes of this
        /// layout do not share a scale: one X unit is the full window width in pixels and one Y
        /// unit is the safe-GUI height (ADR-0009). A horizontal size therefore has to convert
        /// through the horizontal scale, or the caret comes out roughly a third too wide on a 16:9
        /// window — which is exactly what dividing by the height did. The same aspect correction is
        /// what `PortGeometry` applies to its horizontal extents.
        ///
        /// Falls back to the vertical reference before the first sync (client width still 0),
        /// which keeps the caret drawable rather than zero-width for one frame.
        /// </summary>
        private static float CaretThickness
        {
            get
            {
                float width = VgsScreenDraw.ClientWidth;
                if (width <= 0f) return VgsConstants.CaretWidth / VgsConstants.ReferenceHeightPixels;
                return VgsConstants.CaretWidth / width;
            }
        }

        /// <summary>
        /// Draws the modes popup above its button: one toggle row with a check mark, the way a
        /// menu of view options reads everywhere else (ADR-0023).
        ///
        /// Drawn after the canvas and after the bar, because it overlaps both: it hangs above the
        /// bar's top edge, and anything drawn later would cover it.
        /// </summary>
        private void DrawModesMenu()
        {
            Vector2 menuTopLeft, menuSize;
            GetModesMenuRect(out menuTopLeft, out menuSize);

            // Panel rather than Rect + Border, for the same reason as the action menu: the fill
            // must not be able to round a row past the frame (ADR-0024 §5).
            VgsScreenDraw.Panel(menuTopLeft, menuSize, ColMenu, ColMenuBorder, Hairline);

            for (int i = 0; i < ModesMenuRowCount; i++) DrawModesRow(i);
        }

        /// <summary>
        /// Draws one toggle row: the check square, its filled interior when the option is on, and
        /// the label. The label of row 0 is "show/hide invisible", row 1 "show subgrids", row 2
        /// "show grid borders" (ADR-0023, ADR-0032).
        /// </summary>
        private void DrawModesRow(int index)
        {
            Vector2 rowTopLeft, rowSize;
            GetModesRowRect(index, out rowTopLeft, out rowSize);

            if (_modesRowHot == index) VgsScreenDraw.Rect(rowTopLeft, rowSize, ColMenuRowHot);

            bool on = ModesRowValue(index);

            float checkLeft = rowTopLeft.X + VgsConstants.ModesMenuPadding;
            float checkTop = rowTopLeft.Y + (rowSize.Y - VgsConstants.ModesMenuCheckSize) * 0.5f;

            VgsScreenDraw.Border(new Vector2(checkLeft, checkTop),
                                 new Vector2(VgsConstants.ModesMenuCheckSize,
                                             VgsConstants.ModesMenuCheckSize),
                                 on ? ColNodeSelected : ColMenuBorder, Hairline);

            if (on)
            {
                // A filled square inside the frame rather than a tick glyph: the editor's font
                // may be any of several, and a tick that is missing from one of them draws as a
                // box — the same reasoning that made the port's remove mark a Latin "x".
                float inset = VgsConstants.ModesMenuCheckSize * 0.25f;
                VgsScreenDraw.Rect(new Vector2(checkLeft + inset, checkTop + inset),
                                   new Vector2(VgsConstants.ModesMenuCheckSize - inset * 2f,
                                               VgsConstants.ModesMenuCheckSize - inset * 2f),
                                   ColNodeSelected);
            }

            float labelLeft = checkLeft + VgsConstants.ModesMenuCheckSize + VgsConstants.ModesMenuPadding;
            float labelWidth = rowTopLeft.X + rowSize.X - labelLeft - VgsConstants.ModesMenuPadding;
            if (labelWidth <= 0f) return;

            float labelTop = rowTopLeft.Y + (rowSize.Y - ModesMenuLabelHeight) * 0.5f;
            if (labelTop < rowTopLeft.Y) labelTop = rowTopLeft.Y;

            VgsScreenDraw.Text(ModesRowLabel(index), new Vector2(labelLeft, labelTop),
                               VgsConstants.ModesMenuLabelScale, ColText, labelWidth);
        }

        /// <summary>Whether the option in a modes-menu row is currently on.</summary>
        private bool ModesRowValue(int index)
        {
            switch (index)
            {
                case 0: return _view.ShowHidden;
                case 1: return _view.ShowSubgrids;
                case 2: return _view.ShowGridBorders;
                case 3: return _view.ShowConveyors;
            }
            return false;
        }

        /// <summary>Label of a modes-menu row.</summary>
        private static string ModesRowLabel(int index)
        {
            switch (index)
            {
                case 0: return VgsText.ModesShowHidden;
                case 1: return VgsText.ModesShowSubgrids;
                case 2: return VgsText.ModesShowGridBorders;
                case 3: return VgsText.ModesShowConveyors;
            }
            return string.Empty;
        }

        /// <summary>
        /// The warning to show, or null once it has expired. Expiry is checked here rather
        /// than by a timer so nothing has to run between frames.
        /// </summary>
        private string ActiveWarning
        {
            get
            {
                if (_warningText == null) return null;
                if (_screenFrame > _warningUntilFrame) { _warningText = null; return null; }
                return _warningText;
            }
        }

        /// <summary>
        /// The bar's hint line. Bind mode, watch mode and the manual action menu replace the idle
        /// hint, because while those modes are on the only thing the user needs to know is how to
        /// finish or cancel — and on a yes/no toolbar, which of the two lists the connection is
        /// going into.
        ///
        /// A pointer over the jump button, the modes button or the search field replaces it too
        /// (ADR-0021, ADR-0023): each of those either closes the screen or changes what the whole
        /// canvas shows, and that belongs in front of the player before the click.
        /// </summary>
        private string BottomBarHint
        {
            get
            {
                if (_watchSource != null) return VgsText.WatchHint;

                // The manual menu says what a row does — nothing is written, the action runs —
                // because that is the one thing it does *not* share with the bind menu that looks
                // exactly like it (ADR-0041).
                if (_menuMode == ActionMenuMode.Manual) return VgsText.ManualActionHint;

                if (_bindSource == null)
                {
                    if (_hoverButton != null && _hoverButtonKind == CardButtonKind.Jump)
                    {
                        return VgsText.JumpHint;
                    }

                    // The power switch explains itself before the click: what it does while it can
                    // do something, and why it will refuse while it cannot (ADR-0035).
                    if (_hoverPowerSwitch != null)
                    {
                        return _hoverPowerSwitch.State.Power == BlockPower.Unavailable
                            ? VgsText.PowerUnavailableHint
                            : VgsText.PowerHint;
                    }

                    if (_modesHot) return VgsText.ModesHint;
                    if (_searchField.Hot || _searchField.ClearHot) return VgsText.SearchHint;

                    // No idle hint. The editor's gestures are carried by the controls
                    // themselves — an arrow, a bell, a lightbulb, an "x" — and a permanent
                    // sentence describing them was the first thing every player had to read
                    // past (ADR-0023).
                    return string.Empty;
                }
                if (_bindGroup == PortGroup.Yes) return VgsText.BindHintTrue;
                if (_bindGroup == PortGroup.No) return VgsText.BindHintFalse;
                return VgsText.BindHint;
            }
        }

        /// <summary>
        /// Draws the Close button. The screen-space API has no controls, so this is a filled
        /// rectangle with a border and a centred label; the click is handled in
        /// <see cref="HandleCloseButton"/> against the same rectangle.
        /// </summary>
        private void DrawCloseButton()
        {
            Vector2 topLeft, size;
            GetCloseButtonRect(out topLeft, out size);

            // Panel rather than Rect + Border: the fill sits under the frame instead of sharing
            // its nominal bottom edge, so it cannot round a row lower than the border (ADR-0024 §5).
            VgsScreenDraw.Panel(topLeft, size, _closeHot ? ColNodeBorder : ColNode, ColNodeSelected,
                                Hairline);

            VgsScreenDraw.TextCentered(VgsText.CloseButton,
                                       topLeft + new Vector2(size.X * 0.5f, size.Y * 0.5f),
                                       0.7f, ColText);
        }

        /// <summary>
        /// Draws the header: "Workbench" and, on the same line, the counts of what is on the
        /// canvas (ADR-0023).
        ///
        /// The statistics used to be the first line of the bottom bar, in Russian. They moved up
        /// here and into English because the header is what reads as a tool's title bar: the name
        /// of the window and what it currently contains, in one line, and the bottom bar left for
        /// the controls the player operates. The grid's name stays right-aligned on the same line,
        /// so the header is one row at every resolution.
        /// </summary>
        private void DrawHeader()
        {
            VgsScreenDraw.Rect(Vector2.Zero, new Vector2(1f, VgsConstants.HeaderHeight), ColBottomBar);

            VgsScreenDraw.Text(VgsText.EditorTitle,
                               new Vector2(VgsConstants.HeaderLeftMargin, VgsConstants.HeaderTitleTop),
                               VgsConstants.HeaderTitleScale, ColText);

            string gridName = GetGridName();
            float gridNameWidth = gridName != null ? GridNameWidth : 0f;
            float gridNameLeft = 1f - VgsConstants.HeaderLeftMargin - gridNameWidth;

            // A name wider than the header would otherwise be drawn starting left of the margin,
            // on top of the title. Clamping it is the honest degradation: the name is clipped by
            // its own left edge rather than painted over "Workbench".
            if (gridNameLeft < VgsConstants.HeaderLeftMargin) gridNameLeft = VgsConstants.HeaderLeftMargin;

            // The counts follow the title on the same baseline. Their X comes from the title's
            // measured width, which is the only way to keep the two from colliding when the font
            // differs: the screen-space API has no text flow, so a second string is placed by hand.
            //
            // They are also clipped to the span left of the grid's name. Without that, a ship with
            // a long name and a dense schema would draw the two strings over each other in the
            // middle of the header — the one place in this window where two pieces of text share a
            // line and neither can move.
            float statsLeft = VgsConstants.HeaderLeftMargin + TitleWidth;
            float statsWidth = gridNameLeft - VgsConstants.CardButtonTextGap - statsLeft;

            if (TitleWidth > 0f && statsWidth > 0f)
            {
                VgsScreenDraw.Text(GetStatusText(),
                                   new Vector2(statsLeft, VgsConstants.HeaderTitleTop),
                                   VgsConstants.HeaderTitleScale, ColTextDim, statsWidth);
            }

            if (gridName == null) return;

            VgsScreenDraw.Text(gridName,
                               new Vector2(gridNameLeft, VgsConstants.HeaderGridNameTop),
                               VgsConstants.HeaderGridNameScale, ColTextDim,
                               1f - VgsConstants.HeaderLeftMargin - gridNameLeft);
        }

        /// <summary>
        /// Width of the header's title, measured once.
        ///
        /// Cached because <see cref="DrawHeader"/> runs every frame and the title is a constant
        /// string at a constant scale: measuring it per frame would call into the font metrics
        /// sixty times a second for a number that cannot change while the editor is open. The
        /// cache is keyed by the client width, which is the only input that can move the answer —
        /// the editor is modal, so a resolution change closes and reopens it.
        /// </summary>
        private float TitleWidth
        {
            get
            {
                float width = VgsScreenDraw.ClientWidth;
                if (_titleWidth > 0f && _titleWidthClientWidth == width) return _titleWidth;

                // The title is a constant string, so a zero here means the font refused to
                // measure (no font resource yet, or a measurement that threw). That is not cached:
                // caching it would hide the header's statistics for the rest of the session over
                // one bad frame, and the next frame costs a single measurement to retry.
                float measured = VgsScreenDraw.Measure(VgsText.EditorTitle, VgsConstants.HeaderTitleScale).X;
                if (measured <= 0f) return 0f;

                _titleWidthClientWidth = width;
                _titleWidth = measured;
                return _titleWidth;
            }
        }

        /// <summary>Cached title width, and the client width it was measured at.</summary>
        private float _titleWidth = -1f;
        private float _titleWidthClientWidth = -1f;

        /// <summary>
        /// Width of the grid's name in the header, measured once per name and client width.
        ///
        /// The header draws the name right-aligned, which means it has to know how wide it is; and
        /// it also needs that width to know how much room is left for the counts. Measuring it
        /// once here serves both and keeps the per-frame cost of a header with a long ship name at
        /// two float comparisons.
        ///
        /// The name is compared by reference: <see cref="ResolveGridName"/> assigns the cache once
        /// in <see cref="OnOpened"/> and nothing else writes it, so a new string there is a new
        /// measurement, and the same string is free.
        /// </summary>
        private float GridNameWidth
        {
            get
            {
                string name = GetGridName();
                if (name == null) return 0f;

                float clientWidth = VgsScreenDraw.ClientWidth;
                if (_gridNameWidth > 0f
                    && ReferenceEquals(_gridNameWidthFor, name)
                    && _gridNameWidthClientWidth == clientWidth)
                {
                    return _gridNameWidth;
                }

                float measured = VgsScreenDraw.Measure(name, VgsConstants.HeaderGridNameScale).X;
                if (measured <= 0f) return 0f;   // not cached: a failed measure must be retried

                _gridNameWidthFor = name;
                _gridNameWidthClientWidth = clientWidth;
                _gridNameWidth = measured;
                return _gridNameWidth;
            }
        }

        /// <summary>Cached grid-name width, the string it was measured for, and the client width.</summary>
        private float _gridNameWidth = -1f;
        private string _gridNameWidthFor;
        private float _gridNameWidthClientWidth = -1f;

        // ------------------------------------------------------------------ cached strings

        /// <summary>
        /// Builds the statistics line only when a value it shows actually changed. Called from
        /// <see cref="OnDraw"/>, i.e. every frame, so an unconditional rebuild here would
        /// allocate a string per frame for the whole time the editor is open.
        /// </summary>
        private string GetStatusText()
        {
            int zoomPercent = (int)(_view.Zoom * 100);
            int nodeCount = _graph.NodeCount;
            int groupCount = _graph.GroupCount;
            int edgeCount = _graph.EdgeCount;

            if (_statusText != null
                && _statusKeyNodes == nodeCount
                && _statusKeyGroups == groupCount
                && _statusKeyEdges == edgeCount
                && _statusKeyZoom == zoomPercent)
            {
                return _statusText;
            }

            _statusKeyNodes = nodeCount;
            _statusKeyGroups = groupCount;
            _statusKeyEdges = edgeCount;
            _statusKeyZoom = zoomPercent;
            _statusText = string.Format(VgsText.StatsFormat, nodeCount, groupCount, edgeCount, zoomPercent);
            return _statusText;
        }

        /// <summary>
        /// Line height of the search field's text, measured once and cached.
        ///
        /// The field centres its text vertically, which needs the height of a line, and
        /// <see cref="DrawSearchField"/> runs every frame. Measured on first use rather than in
        /// the constructor: the font is resolved by <see cref="VgsScreenDraw.EnsureResources"/>,
        /// which may not have run when the screen object is created.
        /// </summary>
        private float SearchLineHeight
        {
            get
            {
                if (_searchLineHeight < 0f)
                {
                    _searchLineHeight =
                        VgsScreenDraw.Measure(LabelProbeText, VgsConstants.SearchTextScale).Y;
                    if (_searchLineHeight <= 0f) _searchLineHeight = 0f;
                }
                return _searchLineHeight;
            }
        }

        /// <summary>
        /// Line height of the modes menu's label, measured once and cached.
        ///
        /// A field of its own, not shared with <see cref="SearchLineHeight"/> or
        /// <see cref="HintLineHeight"/>: the three are drawn at three different font scales, and
        /// one cache served by three scales would return the first one measured to the other two.
        /// </summary>
        private float ModesMenuLabelHeight
        {
            get
            {
                if (_modesLineHeight < 0f)
                {
                    _modesLineHeight =
                        VgsScreenDraw.Measure(LabelProbeText, VgsConstants.ModesMenuLabelScale).Y;
                    if (_modesLineHeight <= 0f) _modesLineHeight = 0f;
                }
                return _modesLineHeight;
            }
        }

        /// <summary>
        /// Line height of the bottom bar's hint line, measured once and cached. Used to centre the
        /// hint in the strip above the bar's controls.
        /// </summary>
        private float HintLineHeight
        {
            get
            {
                if (_hintLineHeight < 0f)
                {
                    _hintLineHeight =
                        VgsScreenDraw.Measure(LabelProbeText, VgsConstants.HintTextScale).Y;
                    if (_hintLineHeight <= 0f) _hintLineHeight = 0f;
                }
                return _hintLineHeight;
            }
        }

        /// <summary>
        /// Reads the grid name once, outside the draw path. Called from <see cref="OnOpened"/>.
        ///
        /// CustomName is a live game property, so it is read here and cached; drawing must only
        /// consume the snapshot (ADR-0003). The read is guarded because the grid can be gone by
        /// the time the screen opens — the ship may have been destroyed between the terminal
        /// click and this call — and a missing name is not worth an exception.
        /// </summary>
        private void ResolveGridName()
        {
            string name = null;
            try
            {
                name = _grid != null ? _grid.CustomName : null;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("gridname", "could not read grid name", e, 600);
            }

            _gridNameCache = string.IsNullOrEmpty(name) ? VgsText.GridFallbackName : name;
        }

        /// <summary>
        /// Grid name for the header. A cache read only — see <see cref="ResolveGridName"/> for
        /// why the world is not touched here.
        /// </summary>
        private string GetGridName()
        {
            return _gridNameCache;
        }
    }
}