using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using Vgs.Core;
using Vgs.Game;
using Vgs.Model;

namespace Vgs.Ui
{
    /// <summary>
    /// Block search: the query, its terms, and the rule that decides which cards are dimmed
    /// (ADR-0023).
    ///
    /// A partial of <see cref="VgsEditorScreen"/>. The field itself — text, caret, selection,
    /// scroll and the cached positions the draw path reads — is <see cref="SearchField"/>; the
    /// filtering rule is <see cref="Vgs.Model.BlockSearch"/>. This file is what is left: the
    /// screen's half, which decides what a query *means* for the picture.
    ///
    /// <b>Why the split.</b> The field is a self-contained editing widget with a rule that must
    /// hold at every keystroke ("the layout is stale after any edit"), and it is exactly the part
    /// that can be checked without the game. The screen keeps the part that needs the graph:
    /// which cards are dimmed, which line stays visible, where Enter jumps.
    /// </summary>
    public sealed partial class VgsEditorScreen
    {
        // ------------------------------------------------------------------ block search (ADR-0023)

        /// <summary>True while a query is being filtered by.</summary>
        private bool SearchActive
        {
            get { return _searchTerms.Count > 0; }
        }

        /// <summary>The query as typed. Never null.</summary>
        private string SearchText
        {
            get { return _searchField.Text; }
        }

        /// <summary>True while the search field holds any text, which is when "x" is drawn.</summary>
        private bool SearchHasText
        {
            get { return _searchField.HasText; }
        }

        /// <summary>
        /// Re-splits the terms after the field's text changed, and clears the field's "changed"
        /// mark — this is the one place that consumes it.
        ///
        /// Called from every edit path, so the dimming follows a keystroke within the same frame;
        /// never from the draw path, because <see cref="BlockSearch.SplitTerms"/> allocates one
        /// string per term.
        /// </summary>
        private void OnSearchTextChanged()
        {
            _searchField.TakeChanged();
            BlockSearch.SplitTerms(_searchField.Text, _searchTerms);
        }

        /// <summary>
        /// Brings the terms up to date if the query changed through a path that did not call
        /// <see cref="OnSearchTextChanged"/> itself. Called from <see cref="OnUpdate"/>, so the
        /// filter is never rebuilt in a frame path.
        ///
        /// A safety net rather than the main route: every edit site syncs immediately, and this
        /// exists so that a future edit path cannot leave the picture filtering by a stale query.
        /// </summary>
        private void SyncSearchTerms()
        {
            if (_searchField.TakeChanged()) BlockSearch.SplitTerms(_searchField.Text, _searchTerms);
        }

        /// <summary>Drops the query, which brings every card back to full opacity.</summary>
        private void ClearSearch()
        {
            _searchField.Clear(_screenFrame);
            OnSearchTextChanged();
        }

        /// <summary>
        /// Inserts one typed character at the caret. Control characters are dropped: the game
        /// hands over the raw character stream, which includes the carriage return of Enter, the
        /// backspace and whatever a Ctrl shortcut produces (ADR-0024).
        /// </summary>
        private void AppendSearchChar(char c)
        {
            if (!_searchField.Insert(c, _screenFrame)) return;

            OnSearchTextChanged();
        }

        /// <summary>
        /// Copies the selection (or the whole query when nothing is selected) to the clipboard.
        /// With <paramref name="cut"/> it also removes what it copied.
        ///
        /// With no selection a copy takes the whole query — what a player expects from a one-line
        /// field — but a *cut* with no selection does nothing. Cutting everything when the player
        /// only pressed Ctrl+X in passing would empty the field they were still reading; the
        /// standard behaviour of every text field is the safer one here.
        /// </summary>
        private void CopySearchText(bool cut)
        {
            if (!_searchField.HasSelection && cut) return;

            string text = _searchField.HasSelection
                ? _searchField.SelectedText
                : _searchField.Text;
            if (string.IsNullOrEmpty(text)) return;

            TextClipboard.Copy(text);

            if (!cut) return;

            if (!_searchField.DeleteForward(_screenFrame)) return;

            OnSearchTextChanged();
        }

        /// <summary>
        /// Pastes the editor's own clipboard at the caret.
        ///
        /// The buffer is this session's own last copy or cut. The operating system's clipboard is
        /// not reachable from a mod — measured, not assumed: the game refuses to load
        /// `IVRageSystem`/`IVRagePlatform`/`MyVRage` as "is prohibited" (ADR-0027). So a paste can
        /// only reproduce text this field itself produced.
        ///
        /// Nothing is shown when the buffer is empty: the player pressed a key that does nothing,
        /// which needs no explanation on a bar whose only free line belongs to failures.
        /// </summary>
        private void PasteSearchText()
        {
            string text = TextClipboard.PasteText;
            if (!_searchField.Insert(text, _screenFrame)) return;

            OnSearchTextChanged();
        }

        /// <summary>
        /// True when the card must be drawn at <see cref="VgsConstants.SearchDimOpacity"/>.
        ///
        /// The two fields are the ones the terminal searches: the block's name and its
        /// <em>displayed type</em> (ADR-0023). A group is dimmed only when none of its drawn
        /// members match: the frame is a backdrop for its members, so it has to follow them
        /// rather than the query — a frame that disappeared while one of its cards stayed would
        /// break the picture apart.
        /// </summary>
        private bool IsDimmed(Node node)
        {
            if (!SearchActive || node == null) return false;

            if (!node.IsGroup)
            {
                return !Matches(node);
            }

            for (int i = 0; i < node.Members.Count; i++)
            {
                Node member = _graph.GetNode(node.Members[i]);
                if (member == null) continue;
                if (Matches(member)) return false;
            }
            return true;
        }

        /// <summary>
        /// True when one node matches the current query: every term must occur in its name or in
        /// its displayed type.
        ///
        /// A single place for the two-field call, so the draw path, the group walk and the Enter
        /// jump cannot end up searching slightly different things — the classic way a search
        /// starts disagreeing with itself.
        /// </summary>
        private bool Matches(Node node)
        {
            if (node == null) return false;
            return BlockSearch.Matches(_searchTerms, node.DisplayName, node.TypeDisplayName);
        }

        /// <summary>Alpha multiplier for one node's drawing: 1 for a match, the dim value otherwise.</summary>
        private float AlphaOf(Node node)
        {
            return IsDimmed(node) ? VgsConstants.SearchDimOpacity : 1f;
        }

        /// <summary>
        /// Alpha of a line between two nodes: the <b>higher</b> of the two.
        ///
        /// A line stays visible while <em>at least one</em> of its ends matches the query, because
        /// the whole point of finding a block is seeing what it is wired to: ghosting the wires of
        /// the card the player just found would hide the one thing they searched for. Only a line
        /// whose both ends are excluded becomes a ghost itself (ADR-0023 §4, and the rejected
        /// alternative there is exactly this mistake).
        /// </summary>
        private float MaxAlpha(Node a, Node b)
        {
            float first = AlphaOf(a);
            float second = AlphaOf(b);
            return second > first ? second : first;
        }

        /// <summary>
        /// Applies <see cref="AlphaOf"/> to a colour. A struct copy, so this is safe in the draw
        /// path: nothing is allocated and no cached colour is mutated.
        /// </summary>
        private static Color Fade(Color color, float alpha)
        {
            if (alpha >= 1f) return color;

            float a = (color.A / 255f) * alpha;
            if (a < 0f) a = 0f;
            return new Color(color, a);
        }

        /// <summary>
        /// Moves the camera so the first card matching the query is centred on the canvas.
        ///
        /// "First" is the graph's own order, which is stable for a given construct — the same
        /// order the cards are drawn in. Nothing happens when nothing matches: the player is
        /// mid-query and the picture must not jump away from them.
        ///
        /// A card whose centre cannot be computed is <em>skipped</em>, not treated as the end of
        /// the walk: one degenerate node must not make Enter a no-op while a perfectly good
        /// match sits further down the list.
        /// </summary>
        private void JumpToFirstMatch()
        {
            if (!SearchActive) return;

            Vector2 canvasTopLeft, canvasSize;
            CanvasRect.Get(out canvasTopLeft, out canvasSize);

            IList<Node> nodes = _graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                if (!Matches(node)) continue;

                Vector2 centre;
                if (!PortGeometry.TryCardCentre(node, out centre)) continue;

                // Aim the camera so the card's centre lands at the centre of the canvas:
                // screen = (graph - camera) * zoom + canvasTopLeft, and the screen point we want
                // is the canvas centre, so camera = graph - canvasSize / (2 * zoom).
                _view.Camera = centre - (canvasSize * 0.5f) / _view.Zoom;

                // Enter picks the one card it jumped to: the query is a way of finding a block,
                // and the block found is the answer. A set the player had built by hand is
                // replaced rather than added to, exactly as a plain click on a card would do
                // (ADR-0048).
                _selection.Select(node);

                VgsLog.Debug("search '" + SearchText + "' jumped to block " + node.EntityId);
                return;
            }
        }

        /// <summary>
        /// Drops references to nodes the refresh has removed.
        ///
        /// A refresh can delete the block the user is currently dragging, binding from, or
        /// picking an action for — and it can delete a *group* the user is aiming at, because a
        /// group disappears as soon as its last drawn member is hidden or destroyed. Those
        /// fields are plain references, so they stay valid as objects after the node leaves the
        /// graph — which means the draw path would happily keep painting a card that no longer
        /// exists. The check is by node identity, not by entity id: a group has no id to ask
        /// about.
        /// </summary>
        private void ValidateInteractionState()
        {
            // The selection is a set of references, and a refresh can drop any of them — a block
            // destroyed, or a group that lost its last drawn member. Pruned rather than rebuilt:
            // the player's selection is theirs, and losing all of it because one card disappeared
            // would be a worse answer than losing that one (ADR-0048).
            _selection.RemoveMissing(_graph);

            // A dragged card or group that vanished takes its drag with it: the list of cards the
            // selection drag was moving is the same set of references, so a card that disappeared
            // mid-drag must not be written back to the world on release.
            if (_dragCards.Count > 0)
            {
                for (int i = _dragCards.Count - 1; i >= 0; i--)
                {
                    if (!_graph.ContainsNode(_dragCards[i])) _dragCards.RemoveAt(i);
                }
                if (_dragCards.Count == 0) _dragging = false;
            }

            if (_draggedNode != null && !_graph.ContainsNode(_draggedNode)) _draggedNode = null;

            // The card a click is about to collapse the selection to: if it disappeared, the
            // release must not select a node that is no longer drawn.
            if (_pendingSelectNode != null && !_graph.ContainsNode(_pendingSelectNode))
            {
                _pendingSelectNode = null;
            }

            // A refresh can dissolve the group the user is dragging — its last drawn member was
            // hidden, or the group was renamed in the terminal. The reference stays valid as an
            // object, so without this the drag would keep translating a frame that is no longer
            // on the canvas.
            if (_draggedGroup != null && !_graph.ContainsNode(_draggedGroup))
            {
                _draggedGroup = null;
                _dragging = false;
            }

            if (_hoverRemoveNode != null)
            {
                // The refresh can remove the block *and* rebuild its port list (a changed action
                // set replaces every Port object). Re-resolve by key instead of comparing
                // references: the gesture is still meaningful as long as the port survives, and
                // a stale object would leave the highlight on a bar that no longer exists.
                if (!_graph.ContainsNode(_hoverRemoveNode))
                {
                    _hoverRemoveNode = null;
                    _hoverRemovePort = null;
                    _hoverRemoveBar = null;
                    _hoverEditNode = null;
                    _hoverEditBar = null;
                }
                else if (_hoverRemovePort != null)
                {
                    Port live = _hoverRemovePort.Direction == PortDirection.Output
                        ? _hoverRemoveNode.FindEvent(_hoverRemovePort.Key)
                        : _hoverRemoveNode.FindAction(_hoverRemovePort.Key);
                    _hoverRemovePort = live;
                    if (live == null) _hoverRemoveNode = null;
                }
                else if (_hoverRemoveBar != null)
                {
                    // An input bar is rebuilt by the same layout pass, so the object is stale even
                    // when its node is not. Re-resolve by the request it carries (ADR-0050).
                    InputBar live = _hoverRemoveNode.FindInputBar(_hoverRemoveBar.Port.Key,
                                                                  _hoverRemoveBar.Parameters);
                    _hoverRemoveBar = live;
                    if (live == null) _hoverRemoveNode = null;
                }
            }

            if (_hoverEditNode != null)
            {
                if (!_graph.ContainsNode(_hoverEditNode))
                {
                    _hoverEditNode = null;
                    _hoverEditBar = null;
                }
                else if (_hoverEditBar != null)
                {
                    InputBar live = _hoverEditNode.FindInputBar(_hoverEditBar.Port.Key,
                                                                _hoverEditBar.Parameters);
                    _hoverEditBar = live;
                    if (live == null) _hoverEditNode = null;
                }
            }

            if (_bindSource != null && !_graph.ContainsNode(_bindSource)) CancelBind();

            // The card the bind was aimed at can go away too, and the pending line ends on it: a
            // line to a card that is no longer in the graph would point at empty canvas while the
            // action menu still listed the block's actions (ADR-0052 §5). The gesture is abandoned
            // rather than re-aimed — there is nothing left to aim at.
            if (_bindTarget != null && !_graph.ContainsNode(_bindTarget))
            {
                VgsLog.Info("bind cancelled: its target block disappeared");
                CancelBind();
            }

            // A controller can be destroyed, or stop being one, while its watch mode is open.
            // The mode is cancelled rather than silently re-aimed: the port it was going to
            // write into no longer exists.
            if (_watchSource != null && !_graph.ContainsNode(_watchSource)) CancelWatch();

            if (_hoverButton != null && !_graph.ContainsNode(_hoverButton)) _hoverButton = null;

            // The card under a held right button is a reference like the others: a refresh that
            // removes it would make the release open the manual menu of a card that is no longer
            // drawn (ADR-0041). Dropped rather than re-aimed, because the click was aimed at a
            // picture that has changed under the player's hand.
            if (_rightClickNode != null && !_graph.ContainsNode(_rightClickNode)) _rightClickNode = null;

            if (_hoverWatchRemoveNode != null && !_graph.ContainsNode(_hoverWatchRemoveNode))
            {
                _hoverWatchRemoveNode = null;
                _hoverWatchRemoveTarget = null;
            }

            // The refresh rebuilds the watch list, so the hovered target can be a stale object
            // even while its controller is still there. Re-resolve by id rather than by
            // reference: the highlight must follow the entry, not the object that used to be it.
            if (_hoverWatchRemoveNode != null && _hoverWatchRemoveTarget != null)
            {
                _hoverWatchRemoveTarget = _hoverWatchRemoveNode.Controller != null
                    ? _hoverWatchRemoveNode.Controller.FindWatch(_hoverWatchRemoveTarget.EntityId)
                    : null;
                if (_hoverWatchRemoveTarget == null) _hoverWatchRemoveNode = null;
            }

            // A bind holds a specific port object, chosen when it started. A refresh that
            // rebuilt the source's event list (its action set changed, or the block was
            // rebuilt) leaves that object orphaned, and the line would then point at a port
            // the node no longer has. Re-resolve by key instead of cancelling: the gesture is
            // still meaningful as long as the port survives.
            if (_bindSource != null && _bindPort != null)
            {
                Port live = _bindSource.FindEvent(_bindPort.Key);
                if (live == null) CancelBind();
                else _bindPort = live;
            }

            if (_menuTarget != null)
            {
                if (!_graph.ContainsNode(_menuTarget) || !SamePorts(_menuRows, _menuTarget.Actions))
                {
                    // The menu is bound to specific port objects; if the target's action list
                    // was rebuilt they are stale, and a click would connect — or, in the manual
                    // menu, run — the wrong action. Both menus go through the same exit: in bind
                    // mode that ends the gesture too, in manual mode it only closes the menu.
                    DismissActionMenu();
                    return;
                }

                // The fan-in counts in the captions are a snapshot. A refresh can change them
                // (another block was ground down), and a caption that lies about how many
                // sources an action already has is worse than a caption built twice a second
                // while a menu is open. This runs in Update, never in Draw.
                FillMenuRows(_menuTarget);
            }

            // The argument dialog holds the same kind of references as every mode above: a node,
            // a port and a connection. A refresh that rebuilds the target's action list replaces
            // every Port object, and a form built from a stale one would write values for an
            // action the block no longer has. The dialog is therefore abandoned rather than
            // re-aimed — the player was typing into a request that no longer exists (ADR-0050).
            if (_paramForm != null)
            {
                bool gone = !_graph.ContainsNode(_paramTarget);

                if (!gone && _paramAction != null && _paramTarget != null)
                {
                    gone = _paramTarget.FindAction(_paramAction.Key) == null;
                }

                if (!gone && _paramMode == ParameterDialogMode.Create && _paramBindSource != null)
                {
                    gone = !_graph.ContainsNode(_paramBindSource);
                }

                if (gone)
                {
                    VgsLog.Info("parameter dialog closed: its action or block disappeared");
                    CloseParameterDialog();
                    if (_bindSource != null) CancelBind();
                }
            }
        }

        private static bool SamePorts(List<Port> rows, List<Port> ports)
        {
            if (rows.Count != ports.Count) return false;
            for (int i = 0; i < rows.Count; i++)
            {
                if (!ReferenceEquals(rows[i], ports[i])) return false;
            }
            return true;
        }

    }
}

