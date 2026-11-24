using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
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
    ///     why connections survive save and load without a storage format of their own.
    ///
    ///  4. <b>Everything else the node needs comes from the block too.</b> The canvas position
    ///     and the "show in VGS" flag are read through <see cref="NodeStateStore"/> from the
    ///     block's own mod storage, so they follow the block through save/load, blueprints and
    ///     grid splitting (ADR-0012, ADR-0013). A block whose flag is off is not added to the
    ///     graph at all.
    ///
    ///  5. <b>A group is a second pass over the same grid.</b> Block groups are read from the
    ///     grid's terminal system after the blocks, and a group becomes a node only when at
    ///     least one of its members is drawn (ADR-0015). Its action list, in contrast, is
    ///     computed from <em>all</em> members, including hidden ones and ones on a subgrid,
    ///     because that is what the game applies a group action to.
    /// </summary>
    public static class GridGraphBuilder
    {
        /// <summary>
        /// Last known names of watched blocks, used while a controller's list is refreshed.
        ///
        /// A watched block can be gone, or hidden from the schema, and its bar still has to carry
        /// a label. Keeping the previous name in one reusable map avoids both an allocation per
        /// controller per refresh and a bar that turns into a bare entity id.
        /// </summary>
        private static readonly Dictionary<long, string> _nameScratch =
            new Dictionary<long, string>(8);

        /// <summary>
        /// Creates a graph for the given grid, or an empty graph when the grid is
        /// unavailable. The caller owns the result.
        ///
        /// <paramref name="includeHidden"/> is the editor's "show invisible" mode (ADR-0023):
        /// when it is on, a block whose "Show in VGS" flag is off is part of the graph as well.
        /// The flag is not touched — the mode only decides what the schema draws, so turning it
        /// back off hides those cards again and nothing in the world has changed.
        /// </summary>
        public static GridGraph Build(IMyCubeGrid grid, bool includeHidden, bool includeSubgrids)
        {
            long gridId = 0;
            if (grid != null)
            {
                try { gridId = grid.EntityId; }
                catch (Exception e) { VgsLog.Throttled("build-gridid", "could not read grid id", e, 600); }
            }

            var graph = new GridGraph(gridId);
            if (grid == null) return graph;

            Refresh(graph, grid, includeHidden, includeSubgrids);
            return graph;
        }

        /// <summary>
        /// Re-synchronises an existing graph with the grid: adds new blocks, drops destroyed
        /// ones, refreshes names, rebuilds the group nodes and reconciles the edges with the
        /// blocks' toolbars. Positions and edges of surviving nodes are preserved, which is
        /// what makes the editor feel stable while the ship is being edited.
        ///
        /// <b>This is the structural pass.</b> It answers "what is on the grid and how is it
        /// wired", and it is expensive: it walks every slim block, probes toolbars, reads action
        /// lists and re-derives the port slots. It therefore runs on a slow timer
        /// (<c>VgsEditorScreen.RefreshIntervalFrames</c>), while the values that change every
        /// second — damage today, charge and fill level later — go through
        /// <see cref="RefreshState"/>, which reads nothing but those values and rebuilds nothing.
        /// Keeping the two apart is what lets the schema look live without paying for a full
        /// rebuild twice a second (see <c>docs/03-architecture.md</c>, «два прохода»).
        /// </summary>
        public static void Refresh(GridGraph graph, IMyCubeGrid grid, bool includeHidden,
                                   bool includeSubgrids)
        {
            if (graph == null || grid == null) return;

            var live = new HashSet<long>();
            List<IMyBlockGroup> groups = null;

            // True when the block limit cut the enumeration short. It has to outlive the try
            // block: the removal pass below must know that "not in `live`" means "not read".
            bool truncated = false;

            try
            {
                // The domain is the construct's terminal system, not this grid (ADR-0032).
                //
                // This is the same call the groups below already come from, and that is the whole
                // point: `GetTerminalSystemForGrid` returns the system of the *logical group* —
                // the grid plus its subgrids — verified by IL in
                // `docs/01-research/api-grid-construct.md` §1. Enumerating blocks from `grid` alone
                // while reading groups from the construct is what made the two halves of this
                // graph disagree: a group could name a member that had no card, and a toolbar item
                // pointing at a subgrid block was silently dropped because its target had no node.
                //
                // A null system is "could not read", not "there are no blocks": the refresh keeps
                // the picture it has rather than erasing the schema for one bad frame.
                IMyGridTerminalSystem system = ReadTerminalSystem(grid);
                if (system == null) return;

                var blocks = new List<IMyTerminalBlock>();
                system.GetBlocks(blocks);

                // The limit now bounds the construct, not one grid (ADR-0032), so a ship with a
                // dozen subgrids can reach it where it used to be unreachable. Two things follow,
                // and the second one is a fix rather than a feature:
                //
                //  * the player is told, once per refresh at most, that the picture is partial —
                //    a schema that silently shows half a ship reads as a bug in the schema;
                //  * the blocks past the limit are treated as "not read", not "gone", so the
                //    removal pass below does not delete their cards and edges. Dropping them was
                //    the old behaviour, and on a construct-sized domain it would erase a large
                //    part of a laid-out schema the first time a big ship was opened.
                bool truncatedScan = blocks.Count > VgsConstants.MaxGridBlocksScanned;
                truncated = truncatedScan;
                if (truncatedScan)
                {
                    // Throttled by attempts rather than by frames: this runs once per structural
                    // refresh, so the interval is "every Nth refresh" — about a minute of a very
                    // large construct, which is enough to notice and not enough to drown the log.
                    VgsLog.Throttled("graph-block-limit",
                                     "construct has " + blocks.Count + " terminal block(s), only "
                                     + VgsConstants.MaxGridBlocksScanned + " are shown",
                                     30);
                }

                int scanned = 0;
                for (int i = 0; i < blocks.Count && scanned < VgsConstants.MaxGridBlocksScanned; i++)
                {
                    IMyTerminalBlock terminal = blocks[i];
                    if (terminal == null) continue;
                    scanned++;

                    // A block that is still a construction site has no functional terminal
                    // yet; showing it would offer actions that silently do nothing. The terminal
                    // system does not list construction sites at all, so this is belt and braces:
                    // the check costs one branch and covers a block that was completed or removed
                    // between the enumeration and this read.
                    IMyCubeBlock fat = terminal as IMyCubeBlock;
                    if (fat == null) continue;

                    // A block the player turned off in the terminal is not part of the schema
                    // (ADR-0013), unless the "show invisible" mode is on (ADR-0023). It is
                    // deliberately not added to `live`: an existing node for it is then dropped
                    // by the removal pass below, which also drops its edges. Nothing is written
                    // to the block's toolbar, so the wiring keeps working in the game and comes
                    // back the moment the block is shown again.
                    if (!includeHidden && !NodeStateStore.IsShown(terminal)) continue;

                    // A block on another grid of the same construct is a subgrid block
                    // (ADR-0032). It is part of the schema by default; the player can take the
                    // whole subgrid out of the picture, and then it is filtered here rather than
                    // at draw time — "is this node in the graph" is decided while the graph is
                    // built, and a draw-time filter would leave edges pointing at cards that are
                    // not painted.
                    //
                    // 0 means the grid could not be read, which is not a subgrid: the block stays,
                    // because "could not read" is never "does not belong here".
                    long blockGridId = SafeGridId(terminal);
                    if (!includeSubgrids && blockGridId != 0 && blockGridId != graph.GridEntityId)
                    {
                        continue;
                    }

                    live.Add(terminal.EntityId);

                    Node node = graph.GetNode(terminal.EntityId);
                    if (node == null)
                    {
                        graph.AddNode(CreateNode(terminal, blockGridId));
                    }
                    else
                    {
                        UpdateNode(node, terminal);
                    }
                }

                groups = ReadGroups(grid);
            }
            catch (Exception e)
            {
                VgsLog.Error("GridGraphBuilder.Refresh failed", e);
                return;
            }

            // Drop nodes whose blocks are gone. Collect first: RemoveNode mutates the list.
            //
            // <b>Not when the enumeration was cut short.</b> "Not in `live`" then means "not
            // read", and the two are not the same thing: dropping on a partial read would delete
            // the cards and the edges of every block past the limit, and the reconcile pass
            // cannot rebuild them — the edges would come back only for cells it happens to scan.
            // The schema would lose a large part of a laid-out picture on the first refresh of a
            // big construct (ADR-0032). Survivors stay exactly where they are.
            if (!truncated)
            {
                var dead = new List<long>();
                IList<Node> nodes = graph.Nodes;
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (!live.Contains(nodes[i].EntityId)) dead.Add(nodes[i].EntityId);
                }
                for (int i = 0; i < dead.Count; i++) graph.RemoveNode(dead[i]);
            }

            graph.LayoutUnplaced();

            // Groups after the cards: a group's frame is derived from where its members ended
            // up, and a member that was just laid out must already be at its final position.
            SyncGroups(graph, groups);

            ReconcileToolbarEdges(graph);

            // Port bars are a derived view of the edges: a node that just disappeared took
            // its edges with it, so the remaining nodes' stacks have to close up.
            graph.LayoutPorts();

            VgsLog.Debug("graph refresh: " + graph.NodeCount + " nodes, " + graph.GroupCount
                         + " groups, " + graph.EdgeCount + " edges");
        }

        /// <summary>
        /// Refreshes the <em>state</em> of the nodes in <paramref name="entityIds"/>: the values
        /// that change while the editor is open, and nothing else. Returns how many visible cards
        /// it managed to read.
        ///
        /// <b>Two values, one pass.</b> Damage (<c>BuildIntegrity / MaxIntegrity</c>) and the
        /// block's own on/off switch (ADR-0035) are read from the same resolved block, because
        /// they are two questions about one block and the pass runs for every visible card twice
        /// a second. Each is written separately: a block whose integrity read threw can still
        /// have a good switch reading, and losing one must not cost the other.
        ///
        /// <b>What it deliberately does not do:</b> it does not add or remove nodes, does not
        /// touch ports, groups, toolbars or edges, does not reconcile the schema with the world
        /// and does not call <see cref="GridGraph.LayoutPorts"/>. Those are the structural pass's
        /// job (<see cref="Refresh"/>), and re-running them on this cadence would rebuild the
        /// picture under the player's pointer several times a second for no reason.
        ///
        /// <b>Why a list of ids instead of the whole graph.</b> The caller passes the nodes that
        /// are actually on screen (<c>VgsEditorScreen.CollectVisibleNodeIds</c>), so the cost is
        /// proportional to what the player can see rather than to the size of the ship. On a
        /// five-hundred-block grid that is the difference between four reads per tick and five
        /// hundred. The list is reused by the caller, so this method allocates nothing.
        ///
        /// <b>A block that cannot be read keeps its previous value.</b> It is not reset to 1 or to
        /// "off": one failed read is not evidence that the block was repaired or switched, and a
        /// card that flickers between states is worse than one that lags by half a second.
        /// </summary>
        public static int RefreshState(GridGraph graph, IList<long> entityIds)
        {
            if (graph == null || entityIds == null) return 0;

            int written = 0;
            for (int i = 0; i < entityIds.Count; i++)
            {
                // A null node is the normal answer for two different things, and both mean "no
                // news": the block was destroyed since the structural pass, or the id belongs to
                // nothing this graph holds. A group is never reachable here — groups live in the
                // graph's own map keyed by name, and `GetNode` looks only at blocks.
                Node node = graph.GetNode(entityIds[i]);
                if (node == null) continue;

                // The block is resolved once and both values are read from it. They are two
                // questions about one block — damage and the on/off switch (ADR-0035) — and the
                // pass runs twice a second for every visible card, so resolving the entity twice
                // would double the most expensive part of it.
                IMyTerminalBlock block;
                if (!EntityResolver.TryResolveTerminalBlock(node.EntityId, out block)) continue;

                bool wrote = false;

                float integrity;
                if (TryReadIntegrity(block, out integrity)
                    && graph.ApplyIntegrity(node.EntityId, integrity))
                {
                    wrote = true;
                }

                // Only a block that has a switch is asked about it: a conveyor tube answers "off"
                // forever, and writing that would be a claim about a control the card does not
                // have.
                if (node.CanBeEnabled)
                {
                    bool enabled, functional;
                    if (BlockSwitch.TryRead(block, out enabled, out functional)
                        && graph.ApplySwitch(node.EntityId, enabled, functional))
                    {
                        wrote = true;
                    }
                }

                if (wrote) written++;
            }
            return written;
        }

        /// <summary>
        /// Reads a block's integrity as a fraction of its maximum. False when the game cannot
        /// answer, which the caller must treat as "no news", not as "undamaged".
        ///
        /// The value comes from the block's slim block — <c>BuildIntegrity / MaxIntegrity</c> —
        /// and not from <c>DamageRatio</c>: the ratio is the same number inverted, and reading the
        /// two parts keeps the arithmetic visible at the point where the units matter. Verified in
        /// <c>docs/01-research/api-block-state.md</c>.
        /// </summary>
        private static bool TryReadIntegrity(IMyTerminalBlock block, out float integrity)
        {
            integrity = 1f;
            if (block == null) return false;

            try
            {
                IMySlimBlock slim = block.SlimBlock;
                if (slim == null) return false;

                float max = slim.MaxIntegrity;
                if (max <= 0f) return false;         // not a cube block with integrity at all

                integrity = slim.BuildIntegrity / max;
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("state-integrity", "could not read block integrity", e, 600);
                return false;
            }
        }

        private static Node CreateNode(IMyTerminalBlock block, long gridId)
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
                toolbar,
                EventControllerProbe.IsController(block),
                gridId);

            var actions = new List<string>();
            node.HasTerminal = TryReadActions(block, actions);
            BlockPortCatalog.FillActions(node, actions);
            BlockPortCatalog.FillEvents(node);

            // Whether the block has an on/off switch is a fact about its type, read once here with
            // everything else the node needs from the world (ADR-0035). The card's state column is
            // sized from it, so it must not be re-read per frame: a column that appeared and
            // disappeared with the block's power would move the block's name sideways on every
            // switch.
            node.CanBeEnabled = BlockSwitch.CanBeEnabled(block);

            // The localized type name, for the search field (ADR-0023). Read here, with
            // everything else the node needs from the world, so no draw path ever touches it.
            node.TypeDisplayName = SafeTypeDisplayName(block);

            // A controller's mode description and watch list are read here, once, together with
            // everything else the node needs from the world (ADR-0017).
            RefreshControllerState(node, block);

            // The saved canvas position, if any, is applied before the node reaches the graph,
            // so LayoutUnplaced (end of Refresh) sees it as placed and leaves it alone.
            NodeStateStore.ApplyTo(node, block);

            return node;
        }

        private static void UpdateNode(Node node, IMyTerminalBlock block)
        {
            node.DisplayName = SafeName(block);

            // The grid is refreshed, not assumed: a split hands part of a ship to a new grid with
            // a new entity id, and the card must follow it or the grid frame will claim it belongs
            // to the wrong ship part (ADR-0032).
            long gridId = SafeGridId(block);
            if (gridId != 0) node.GridEntityId = gridId;

            // The type line is refreshed with the name: the player can rename a block at any
            // time, and the localized type can change with the game's language. The editor's
            // search reads it (ADR-0023), so it has to follow the world like the name does.
            node.TypeDisplayName = SafeTypeDisplayName(block);

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

            // The selected event, its threshold and the watch list can all change while the
            // editor is open — the player has the terminal next to the schema.
            RefreshControllerState(node, block);
        }

        /// <summary>
        /// Refreshes what an event controller contributes to its card and its watch stack: the
        /// mode description and the list of watched blocks (ADR-0017, ADR-0018).
        ///
        /// <b>The game is the source of truth.</b> The list shown here is the union of what the
        /// controller reports itself (<see cref="EventControllerProbe.TryReadGameWatchIds"/>) and
        /// the entries the editor added through the event component, which the game does not put
        /// into its own list. So a block the player adds, removes or re-targets in the vanilla
        /// terminal appears, disappears or moves here on the next refresh — the schema follows the
        /// game instead of keeping a private copy of it.
        ///
        /// <b>Nothing is deleted because it is not visible.</b> A watched block whose "Show in
        /// VGS" flag is off has no card, so its line cannot be drawn, but it stays in the list and
        /// keeps its port: the schema hides what it cannot draw, it never edits the game's data
        /// to tidy up the picture.
        ///
        /// Names are refreshed for every watched block, including ones with no node of their own:
        /// the watch bar is labelled from this snapshot, and the draw path must not read the world.
        /// </summary>
        private static void RefreshControllerState(Node node, IMyTerminalBlock block)
        {
            // The controller state *is* the flag (ADR-0030): a node without one is not a
            // controller, and there is nothing here for it.
            if (node == null || node.Controller == null) return;

            ControllerState controller = node.Controller;

            controller.StatusText = EventControllerProbe.Describe(block);

            long eventId = EventControllerProbe.SelectedEventId(block);

            // What the controller itself says it watches. Unreadable is not empty: keeping the
            // previous picture for one refresh is better than making every port vanish and
            // reappear, and the next refresh retries.
            var gameIds = new List<long>();
            if (!EventControllerProbe.TryReadGameWatchIds(block, gameIds)) return;

            // What the editor added on top, and the game therefore does not store.
            var overlay = new List<long>();
            NodeStateStore.TryReadWatchList(block, overlay);

            // An id the game now reports is the game's business: drop it from the overlay, or it
            // would be counted twice and replayed after a reload for no reason.
            if (GridGraph.PruneAdoptedWatchIds(gameIds, overlay))
            {
                // Only the server may write; on a client the prune stays in memory and simply
                // repeats next refresh, which is harmless because the union is the same.
                if (EventControllerProbe.IsServer()) NodeStateStore.SaveWatchList(block, overlay);
            }

            var ids = new List<long>();
            GridGraph.MergeWatchIds(gameIds, overlay, ids);

            _nameScratch.Clear();
            for (int i = 0; i < controller.Watched.Count; i++)
            {
                _nameScratch[controller.Watched[i].EntityId] = controller.Watched[i].DisplayName;
            }

            GridGraph.SyncWatched(node, ids, id => SafeNameOf(id, LookupName(_nameScratch, id)));
            // The player picked another event in the terminal: the game built a new event
            // component, and the editor's own additions are subscribed to the old one. Replaying
            // them is what keeps those ports honest after a switch (ADR-0018).
            if (eventId != controller.WatchEventId && overlay.Count > 0 && EventControllerProbe.IsServer())
            {
                VgsLog.Info("event changed on controller " + node.EntityId
                            + "; replaying " + overlay.Count + " editor-added watch(es)");
                EventControllerProbe.ReapplyWatchList(block);
            }

            controller.WatchEventId = eventId;
        }

        /// <summary>
        /// Last known name of a watched block, or null. Used as the fallback for a block that is
        /// gone, so its bar keeps a readable label instead of turning into an entity id.
        /// </summary>
        private static string LookupName(Dictionary<long, string> names, long id)
        {
            string name;
            return names.TryGetValue(id, out name) ? name : null;
        }

        /// <summary>
        /// Name of a block by id, falling back to <paramref name="fallback"/> when it is gone or
        /// cannot be read. A watched block does not have to be on the canvas, so this resolves
        /// the live entity rather than asking the graph.
        /// </summary>
        private static string SafeNameOf(long entityId, string fallback)
        {
            IMyTerminalBlock block;
            if (!EntityResolver.TryResolveTerminalBlock(entityId, out block))
            {
                return fallback ?? entityId.ToString();
            }

            try
            {
                string name = SafeName(block);
                return string.IsNullOrEmpty(name) ? (fallback ?? entityId.ToString()) : name;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("watch-name", "could not read watched block name", e, 600);
                return fallback ?? entityId.ToString();
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

        // ------------------------------------------------------------------ groups

        /// <summary>
        /// The terminal system of the grid's <em>logical group</em> — the construct, i.e. the grid
        /// plus its subgrids — or null when it cannot be read.
        ///
        /// <b>This is the graph's domain, and it is one call for both blocks and groups</b>
        /// (ADR-0032). Verified in <c>docs/01-research/api-grid-construct.md</c> §1: the game
        /// resolves <c>GetTerminalSystemForGrid</c> through the logical group's group data, not
        /// through the single grid. Reading blocks from one place and groups from another is what
        /// made a group able to name a member that had no card on the canvas.
        ///
        /// Null means "could not read", never "there are none": the caller keeps what it has.
        /// </summary>
        private static IMyGridTerminalSystem ReadTerminalSystem(IMyCubeGrid grid)
        {
            try
            {
                if (MyAPIGateway.TerminalActionsHelper == null) return null;
                return MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("read-terminal-system", "could not read the terminal system", e, 600);
                return null;
            }
        }

        /// <summary>
        /// Reads the block groups of the construct's terminal system, or null when they cannot be
        /// read.
        ///
        /// That is deliberate and matches the game: a group may contain blocks from a subgrid, and
        /// the game applies a group action to all of them, so the editor must show the group and
        /// offer the actions it really has.
        /// </summary>
        private static List<IMyBlockGroup> ReadGroups(IMyCubeGrid grid)
        {
            try
            {
                IMyGridTerminalSystem system = ReadTerminalSystem(grid);
                if (system == null) return null;

                var groups = new List<IMyBlockGroup>();
                system.GetBlockGroups(groups);
                return groups;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("read-groups", "could not read block groups", e, 600);
                return null;
            }
        }

        /// <summary>
        /// Rebuilds the group nodes of the graph from the groups read off the grid.
        ///
        /// Two lists per group, and they are different on purpose:
        ///
        ///  * <b>drawn members</b> — members that have a node in the graph. The group exists
        ///    only while there is at least one, which is exactly how "Show in VGS" aggregates
        ///    for a group: all members hidden means the group is hidden, one member shown means
        ///    the group is shown (ADR-0015). The frame is drawn around these cards.
        ///  * <b>all members</b> — the whole group, hidden members and subgrid members
        ///    included. The action list is computed from these, because the game applies the
        ///    action to all of them; computing it from the drawn ones would offer fewer actions
        ///    than the connection really performs.
        ///
        /// A group that has vanished from the terminal system is dropped, together with its
        /// incoming edges. Nothing is written to any toolbar for that: the item stays where it
        /// is, and the connection comes back if the group is restored under the same name.
        ///
        /// <b>"Could not read" is never "does not exist".</b> A group is dropped only when it
        /// was actually read and has no drawn member. Three cases keep the node as it is: the
        /// member budget ran out before the action list was complete, the group is past
        /// <see cref="VgsConstants.MaxGroupsScanned"/>, and the group's own read threw. The
        /// distinction matters more than it looks — a dropped group takes its edges with it, and
        /// the reconcile pass cannot rebuild them because the node is gone, so a false negative
        /// would erase the picture for good while the toolbar still holds the wiring.
        /// </summary>
        private static void SyncGroups(GridGraph graph, List<IMyBlockGroup> groups)
        {
            // A failed read is not "there are no groups": dropping every group node here would
            // delete the picture (and its lines) for one bad frame, and the next reconcile pass
            // would rebuild it from the toolbars — a flash the player would see as a bug. The
            // groups simply stay as they were and are retried on the next refresh.
            if (groups == null) return;

            var alive = new HashSet<string>(StringComparer.Ordinal);
            var undetermined = new HashSet<string>(StringComparer.Ordinal);
            var members = new List<long>();
            var memberActions = new List<GroupMemberActions>();
            var blocks = new List<IMyTerminalBlock>();
            var actionIds = new List<string>();
            var enabledIds = new List<string>();

            int budget = VgsConstants.MaxGroupMembersScanned;
            int count = groups.Count < VgsConstants.MaxGroupsScanned
                ? groups.Count
                : VgsConstants.MaxGroupsScanned;

            for (int g = 0; g < count; g++)
            {
                IMyBlockGroup group = groups[g];
                if (group == null) continue;

                string name;
                try { name = group.Name; }
                catch (Exception e)
                {
                    VgsLog.Throttled("read-group.name", "could not read a block group name", e, 600);
                    continue;
                }
                if (string.IsNullOrEmpty(name)) continue;

                GroupOutcome outcome = SyncOneGroup(graph, group, name, members, memberActions,
                                                   blocks, actionIds, enabledIds, ref budget);

                if (outcome == GroupOutcome.Alive) alive.Add(name);
                else if (outcome == GroupOutcome.Undetermined) undetermined.Add(name);
            }

            // Groups past the scan limit were never looked at, so their fate is unknown. Mark
            // them so they survive the drop below — a ship with more groups than the budget
            // keeps every group it already showed.
            for (int g = count; g < groups.Count; g++)
            {
                IMyBlockGroup group = groups[g];
                if (group == null) continue;
                try { undetermined.Add(group.Name); }
                catch (Exception e) { VgsLog.Throttled("read-group.limit", "could not read a block group name past the scan limit", e, 600); }
            }

            if (groups.Count > count)
            {
                VgsLog.Throttled("group-limit",
                    "group scan limit reached: " + (groups.Count - count) + " group(s) kept as they were", 1800);
            }

            // Drop groups that are gone: not in the terminal system any more, or read
            // successfully and no longer holding a single drawn member. Collect first,
            // RemoveGroup mutates the list.
            var stale = new List<string>();
            IList<Node> existing = graph.Groups;
            for (int i = 0; i < existing.Count; i++)
            {
                string name = existing[i].GroupName;
                if (alive.Contains(name)) continue;
                if (undetermined.Contains(name)) continue;
                stale.Add(name);
            }
            for (int i = 0; i < stale.Count; i++) graph.RemoveGroup(stale[i]);
        }

        /// <summary>What one group's read established.</summary>
        private enum GroupOutcome
        {
            /// <summary>Read, has a drawn member, node updated (or kept as it was).</summary>
            Alive = 0,

            /// <summary>Read, no drawn member: the group is not part of the schema.</summary>
            Dead = 1,

            /// <summary>Not read far enough to decide. The node is kept untouched.</summary>
            Undetermined = 2
        }

        /// <summary>
        /// Reads one group and updates its node.
        ///
        /// <b>Two passes over the members, and the order is the point.</b> The first pass only
        /// asks the graph whether each member is drawn — a dictionary probe, no world access —
        /// and decides whether the group is alive at all. Only the second pass spends the
        /// member budget, and that budget is spent on reading action lists. Doing it the other
        /// way round was a real defect: the loop stopped at the budget, a drawn member that
        /// happened to come later in the set was never seen, and a live group was declared dead
        /// and removed together with its edges. `GetBlocks` returns a `HashSet`, so "later" is
        /// arbitrary — the group would have vanished on some refreshes and not others.
        ///
        /// The buffers belong to the caller and are reused across groups.
        /// </summary>
        private static GroupOutcome SyncOneGroup(GridGraph graph, IMyBlockGroup group, string name,
                                                 List<long> members, List<GroupMemberActions> memberActions,
                                                 List<IMyTerminalBlock> blocks, List<string> actionIds,
                                                 List<string> enabledIds, ref int budget)
        {
            blocks.Clear();
            try
            {
                group.GetBlocks(blocks, null);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("read-group.grid", "could not read a block group", e, 600);
                return GroupOutcome.Undetermined;
            }

            // Pass 1: which members are drawn. Cheap and complete — no budget applies, because
            // this is what decides whether the group exists at all.
            members.Clear();
            for (int b = 0; b < blocks.Count; b++)
            {
                IMyTerminalBlock block = blocks[b];
                if (block == null) continue;

                try
                {
                    if (graph.GetNode(block.EntityId) != null) members.Add(block.EntityId);
                }
                catch (Exception e)
                {
                    VgsLog.Throttled("group-member", "member id failed", e, 600);
                }
            }

            if (members.Count == 0) return GroupOutcome.Dead;

            // Pass 2: the action list, from every member — drawn or not, because the game
            // applies a group action to all of them.
            memberActions.Clear();
            bool truncated = false;

            for (int b = 0; b < blocks.Count; b++)
            {
                IMyTerminalBlock block = blocks[b];
                if (block == null) continue;

                if (budget <= 0)
                {
                    // An action list computed from a partial group is worse than a stale one:
                    // it would offer an action the game will not fire for a member nobody
                    // looked at. The group keeps last refresh's ports and is retried next time.
                    truncated = true;
                    break;
                }
                budget--;

                actionIds.Clear();
                enabledIds.Clear();
                ReadMemberActions(block, actionIds, enabledIds);

                memberActions.Add(new GroupMemberActions(
                    SafeTypeKey(block), IsFunctional(block),
                    new List<string>(actionIds), new List<string>(enabledIds)));
            }

            if (truncated)
            {
                VgsLog.Throttled("group-budget",
                    "group action list left stale: member budget spent", 1800);
                return GroupOutcome.Alive;
            }

            graph.SyncGroup(name, members, memberActions);
            return GroupOutcome.Alive;
        }

        /// <summary>
        /// Reads one member's action ids and which of them are enabled on it.
        ///
        /// The enabled subset is not decoration: the game's own group rule filters actions by
        /// `ITerminalAction.IsEnabled(block)` — "any member" when the group has one type,
        /// "every member" when it has several. Without it the editor would offer an action that
        /// no member will execute, which is the one failure mode this feature exists to avoid.
        /// `IsEnabled` is on the ModAPI mirror, so this costs a whitelist-safe call per action.
        /// </summary>
        private static void ReadMemberActions(IMyTerminalBlock block, List<string> ids, List<string> enabled)
        {
            try
            {
                var actions = new List<Sandbox.ModAPI.Interfaces.ITerminalAction>();
                block.GetActions(actions);
                if (actions == null) return;

                for (int i = 0; i < actions.Count; i++)
                {
                    var action = actions[i];
                    if (action == null) continue;

                    string id = action.Id;
                    if (string.IsNullOrEmpty(id)) continue;

                    ids.Add(id);

                    bool isEnabled;
                    try { isEnabled = action.IsEnabled(block); }
                    catch (Exception e)
                    {
                        // An action that cannot answer is treated as available: hiding it would
                        // make the group's list shrink for a reason the player cannot see.
                        VgsLog.Throttled("group-enabled", "could not read action state", e, 600);
                        isEnabled = true;
                    }
                    if (isEnabled) enabled.Add(id);
                }
            }
            catch (Exception e)
            {
                VgsLog.Throttled("getactions.member", "GetActions failed for a group member", e, 300);
            }
        }

        /// <summary>
        /// Identity of a block's runtime type, used only to compare two members for equality.
        ///
        /// A group's action list is the intersection of its members' action ids when the types
        /// differ (ADR-0015), so the comparison is between type identities. The type name is
        /// enough and keeps this layer from passing a <see cref="System.Type"/> into the model.
        /// </summary>
        private static string SafeTypeKey(IMyTerminalBlock block)
        {
            try
            {
                Type type = block.GetType();
                return type != null ? type.FullName : string.Empty;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("group-type", "could not read block type", e, 600);
                return string.Empty;
            }
        }

        /// <summary>
        /// True when the block is a functional block — the game's own test for a group member,
        /// `block as MyFunctionalBlock != null`. A mixed group with one non-functional member
        /// has no actions at all, so this flag is part of the rule, not a detail.
        /// </summary>
        private static bool IsFunctional(IMyTerminalBlock block)
        {
            try { return block is IMyFunctionalBlock; }
            catch (Exception e)
            {
                VgsLog.Throttled("group-functional", "could not read block kind", e, 600);
                return false;
            }
        }

        // ------------------------------------------------------------------ edges

        /// <summary>
        /// Rebuilds the edges of every toolbar-driven node from the items actually present in
        /// its toolbar.
        ///
        /// Two directions, and both matter: an edge whose cell no longer holds that item is
        /// removed (the player cleared the slot, or the target block was destroyed), and a
        /// cell that holds a known item without an edge gets one (the player wired it in the
        /// terminal, or the graph was just opened on a saved ship).
        ///
        /// Items that point at a block outside the graph — another grid, a block definition —
        /// are left alone: the editor cannot draw them, and deleting them would destroy wiring
        /// the player made by hand. A group item is drawn only while the group node exists,
        /// which is the same "aggregate visibility" rule the group itself follows.
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

                CellContents contents = ToolbarWriter.TryDescribe(toolbar, edge.Cell.Index);

                bool present = edge.TargetsGroup
                    ? contents.HoldsGroupAction(edge.TargetGroupName, edge.TargetPortKey)
                    : contents.HoldsBlockAction(edge.TargetEntityId, edge.TargetPortKey);

                if (!present) graph.RemoveEdge(edge);
            }

            // 2. Add an edge for every known item that has none.
            for (int index = 0; index < layout.CellCount && budget > 0; index++)
            {
                budget--;

                CellContents contents = ToolbarWriter.TryDescribe(toolbar, index);
                if (contents.IsEmpty) continue;

                var cell = new ToolbarCell(layout.ToolbarIndex, index);
                if (graph.FindEdgeInCell(node.EntityId, cell) != null) continue;

                PortGroup group = ToolbarPlanner.GroupOfCell(layout, index);
                Port port = node.PrimaryEventFor(group);
                if (port == null) continue;

                // TryConnect re-checks that the target node and its action port exist, so an
                // item pointing outside the graph is skipped without a special case here.
                if (contents.IsGroupAction)
                {
                    graph.TryConnectToGroup(node.EntityId, port.Key, contents.GroupName,
                                            contents.ActionId, cell);
                }
                else if (contents.IsBlockAction)
                {
                    graph.TryConnect(node.EntityId, port.Key, contents.BlockEntityId,
                                     contents.ActionId, cell);
                }
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
            IMyTerminalBlock block;
            if (!EntityResolver.TryResolveTerminalBlock(entityId, out block)) return null;

            try
            {
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
                VgsLog.Throttled("getactions.block", "GetActions failed", e, 300);
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

        /// <summary>
        /// The block's type as the player sees it — the localized definition name, which is what
        /// the terminal's block list shows and searches (ADR-0023).
        ///
        /// <b>Why not <see cref="SafeSubtype"/>.</b> The subtype is the engine id
        /// ("LargeBlockTimer"): a player never types it, and it is the same string in every
        /// language. The terminal searches the *display* name, so the editor has to as well, or
        /// the two searches disagree on exactly the queries a player is most likely to type —
        /// "timer", "таймер".
        ///
        /// Falls back to the subtype when the game cannot answer: a block that is still loading
        /// its definition, or one whose definition was removed with a mod. An empty string would
        /// silently remove the type from the search, which is a worse failure than searching the
        /// engine id.
        /// </summary>
        private static string SafeTypeDisplayName(IMyTerminalBlock block)
        {
            try
            {
                string name = block.DefinitionDisplayNameText;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("typedefname", "could not read block type name", e, 300);
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

        /// <summary>
        /// Entity id of the grid the block stands on, or 0 when it cannot be read (ADR-0032).
        ///
        /// 0 is not an error and not a reason to drop the block: a card without a grid is simply
        /// drawn without a grid frame. The alternative — treating an unreadable grid as "the
        /// block is gone" — would erase the card and its edges for one bad frame, which is the
        /// failure mode the group sync already refuses to have.
        /// </summary>
        private static long SafeGridId(IMyTerminalBlock block)
        {
            try
            {
                IMyCubeGrid cubeGrid = block.CubeGrid;
                return cubeGrid == null ? 0 : cubeGrid.EntityId;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("grid-id", "could not read the block's grid", e, 600);
                return 0;
            }
        }
    }
}
