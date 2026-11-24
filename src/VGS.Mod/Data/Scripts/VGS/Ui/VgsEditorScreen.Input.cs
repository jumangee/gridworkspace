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
    /// Input: the four pointer modes (idle, bind, watch, menu), the pan and zoom, the drag,
    /// and the hit tests that decide what the pointer is over.
    ///
    /// A partial of <see cref="VgsEditorScreen"/>. The modes are mutually exclusive and each
    /// handler owns the pointer completely; geometry comes from <see cref="PortGeometry"/>.
    /// </summary>
    public sealed partial class VgsEditorScreen
    {
        // ------------------------------------------------------------------ input

        protected override void OnHandleInput()
        {
            try
            {
                // TryCursor reports in client-area coordinates only while this transform is
                // current, and the hit tests below assume exactly those units (ADR-0009).
                VgsScreenDraw.SyncScreenSpace();

                if (MyAPIGateway.Input == null) return;

                // Typing is handled before the pointer, and before the "no cursor" early return:
                // the search field is the editor's only keyboard target, and it must keep working
                // when the pointer happens to be outside the window.
                HandleSearchKeyboard();

                Vector2 mouse;
                _mouseValid = VgsScreenDraw.TryCursor(out mouse);
                if (!_mouseValid) return;

                _mouse = mouse;

                if (HandleCloseButton(mouse)) return;

                // The bottom bar's controls are hit-tested before every mode: they belong to the
                // chrome, and a click on the search field must not also pan the canvas or pick a
                // binding target.
                if (HandleBarInput(mouse)) return;

                if (_menuTarget != null) { HandleMenuInput(mouse); return; }
                if (_bindSource != null) { HandleBindInput(mouse); return; }
                if (_watchSource != null) { HandleWatchInput(mouse); return; }

                HandleCanvasInput(mouse);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("screeninput", "input handling failed", e, 300);
            }
        }

        /// <summary>
        /// Feeds the typed characters, the editing keys and the clipboard shortcuts into the
        /// search field (ADR-0023, ADR-0024).
        ///
        /// <b>The field is always the keyboard target.</b> The editor has no other text input and
        /// no keyboard shortcut but Esc, so there is nothing to take focus from it and no focus
        /// ring to explain. A click on the canvas does not blur it, which is what makes "type
        /// anywhere to filter" work.
        ///
        /// <b>The character stream is per frame.</b> The game hands over the characters typed
        /// since the last input update and swaps its buffer, so reading
        /// <see cref="VRage.ModAPI.IMyInput.TextInput"/> once per frame consumes each character
        /// exactly once (verified by IL, see <c>docs/01-research/api-terminal-search.md</c>).
        ///
        /// <b>Order matters.</b> Control keys are resolved before the character stream, because
        /// Ctrl+C also produces a character (0x03) and Backspace produces 0x08: both would be
        /// dropped by the control-character filter anyway, but resolving the shortcut first is what
        /// makes the *one* intent produce the *one* effect. And the stream is consumed last and
        /// unconditionally — every character must be read out of the buffer each frame whether or
        /// not some other key was handled, or the game would carry it into the next frame.
        /// </summary>
        private void HandleSearchKeyboard()
        {
            VRage.ModAPI.IMyInput input = MyAPIGateway.Input;
            if (input == null) return;

            // While the modes popup is open it owns the keyboard: typing must not edit the query
            // behind it, and Ctrl+V must not paste into a field the player cannot see past the
            // menu. This follows the same rule the canvas already follows (ADR-0023 §4: nothing
            // behind the popup takes input) and the Esc order — each step cancels exactly what the
            // player started last, and the menu is what was started last.
            //
            // The character stream is still drained, and that is the whole reason this is a
            // branch and not an early return: an undrained character is carried into the next
            // frame, so it would appear in the field the moment the menu closed — the keystroke
            // would seem to arrive late rather than to be ignored.
            if (_modesMenuOpen)
            {
                VRage.Collections.ListReader<char> swallowed = input.TextInput;
                if (swallowed.Count > 0)
                {
                    VgsLog.Debug("modes menu swallowed " + swallowed.Count +
                                 " typed character(s)");
                }
                return;
            }

            bool control = input.IsAnyCtrlKeyPressed();
            bool shift = input.IsAnyShiftKeyPressed();

            // ---- clipboard ----
            if (control)
            {
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.C)) CopySearchText(false);
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.X)) CopySearchText(true);
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.V)) PasteSearchText();
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.A)) SelectAllSearchText();
            }

            // ---- caret movement and deletion ----
            //
            // Shift extends the selection, a bare arrow collapses it: the rule lives in
            // TextEditor.MoveHorizontal, so these four lines only carry the intent. The field
            // invalidates its cached layout and restarts the blink itself — that pairing is why
            // the field is a type and not four loose fields on this class.
            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Left))
            {
                _searchField.MoveHorizontal(-1, shift, _screenFrame);
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Right))
            {
                _searchField.MoveHorizontal(1, shift, _screenFrame);
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Home))
            {
                _searchField.MoveTo(0, shift, _screenFrame);
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.End))
            {
                _searchField.MoveTo(_searchField.Text.Length, shift, _screenFrame);
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Delete))
            {
                if (_searchField.DeleteForward(_screenFrame)) OnSearchTextChanged();
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Back))
            {
                if (_searchField.Backspace(_screenFrame)) OnSearchTextChanged();
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Enter))
            {
                JumpToFirstMatch();
            }

            // The stream is always drained, even when a shortcut above already acted on it.
            VRage.Collections.ListReader<char> typed = input.TextInput;
            for (int i = 0; i < typed.Count; i++)
            {
                AppendSearchChar(typed[i]);
            }
        }

        /// <summary>Selects the whole query (Ctrl+A).</summary>
        private void SelectAllSearchText()
        {
            _searchField.SelectAll(_screenFrame);
        }

        /// <summary>
        /// Click handling for the bottom bar: the modes button, its popup, and the search field
        /// with its clear button. Returns true when the click was consumed by the bar.
        /// </summary>
        private bool HandleBarInput(Vector2 mouse)
        {
            bool leftNew = MyAPIGateway.Input.IsNewLeftMousePressed();

            Vector2 buttonTopLeft, buttonSize;
            GetModesButtonRect(out buttonTopLeft, out buttonSize);
            _modesHot = PortGeometry.Contains(buttonTopLeft, buttonSize, mouse);

            // ---- modes popup (drawn above the button, so it is resolved first) ----
            if (_modesMenuOpen)
            {
                // The canvas and the search field are not reachable while the popup is up, so
                // every "pointer is over this" highlight is dropped here rather than left on from
                // before the menu opened: a card that kept its hover border behind an open menu
                // reads as a stuck control.
                ClearHoverHighlights();

                Vector2 rowTopLeft, rowSize;
                GetModesRowRect(out rowTopLeft, out rowSize);
                _modesRowHot = PortGeometry.Contains(rowTopLeft, rowSize, mouse);

                if (leftNew)
                {
                    // The button toggles the popup even while it is open: that is what a menu
                    // button does everywhere else, and without this a second click on it would
                    // fall through to "close on any outside click" and look the same — until the
                    // pointer happens to be over the button and the menu seems to ignore it.
                    if (_modesHot)
                    {
                        _modesMenuOpen = false;
                        return true;
                    }

                    if (_modesRowHot)
                    {
                        ToggleShowHidden();
                        return true;
                    }

                    Vector2 menuTopLeft, menuSize;
                    GetModesMenuRect(out menuTopLeft, out menuSize);

                    // A click inside the popup but not on its row is a miss on the popup, not a
                    // command: the menu stays open. A click anywhere else closes it.
                    if (PortGeometry.Contains(menuTopLeft, menuSize, mouse)) return true;

                    _modesMenuOpen = false;
                    return true;
                }

                return true;
            }

            _modesRowHot = false;

            // ---- modes button ----
            if (leftNew && _modesHot)
            {
                _modesMenuOpen = true;
                return true;
            }

            // ---- search field ----
            Vector2 fieldTopLeft, fieldSize;
            GetSearchFieldRect(out fieldTopLeft, out fieldSize);
            _searchField.Hot = PortGeometry.Contains(fieldTopLeft, fieldSize, mouse);

            // The clear button exists only while there is something to clear, and it is
            // resolved before the field for the same reason the port's "x" is resolved before its
            // card: the smaller target on top of the bigger one has to win.
            Vector2 clearTopLeft, clearSize;
            _searchField.ClearHot = SearchHasText
                                    && TrySearchClearRect(out clearTopLeft, out clearSize)
                                    && PortGeometry.Contains(clearTopLeft, clearSize, mouse);

            if (leftNew && _searchField.ClearHot)
            {
                ClearSearch();
                return true;
            }

            // A click inside the field belongs to the field, never to the canvas underneath it.
            // It also decides where the caret goes, which is the one thing a click in a text field
            // is *for*: without this the field had no way to place the caret except by typing.
            if (leftNew && _searchField.Hot)
            {
                PlaceCaretAtPointer(mouse, fieldTopLeft);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Puts the caret at the character boundary nearest the pointer, and drops any selection.
        ///
        /// The inverse of <see cref="SearchField.EnsureLayout"/>: it walks the character
        /// boundaries that layout already measured and picks the closest one. Walking measured
        /// boundaries rather than dividing by an average character width is what makes a click
        /// land between the two glyphs the player aimed at, whatever the font's spacing is.
        ///
        /// With Shift held it extends the selection instead, which is how a click completes a
        /// selection that was started by keyboard.
        /// </summary>
        private void PlaceCaretAtPointer(Vector2 mouse, Vector2 fieldTopLeft)
        {
            float textLeft = fieldTopLeft.X + VgsConstants.SearchFieldPadding;
            // A click anywhere in the field (including on the padding, or on the right half of the
            // space the "x" does not occupy) scrolls the text, so the pointer has to be converted
            // through the current scroll to reach a text coordinate.
            float targetX = mouse.X - textLeft + _searchField.ScrollX;

            _searchField.PlaceCaretAt(targetX, MyAPIGateway.Input.IsAnyShiftKeyPressed(),
                                      _screenFrame);
        }

        /// <summary>
        /// Switches the "show invisible" mode: rebuilds the graph, since whether a hidden block is
        /// part of the schema is decided while it is built (ADR-0023), and writes the new view so
        /// the mode survives closing the editor (ADR-0022).
        /// </summary>
        private void ToggleShowHidden()
        {
            _view.ShowHidden = !_view.ShowHidden;

            RebuildGraph();
            SaveView();

            VgsLog.Info("show invisible blocks: " + (_view.ShowHidden ? "on" : "off"));
        }


        /// <summary>
        /// Click handling for the self-drawn Close button. Returns true when the click was
        /// consumed, so it cannot also start a drag on the canvas underneath.
        /// </summary>
        private bool HandleCloseButton(Vector2 mouse)
        {
            Vector2 buttonTopLeft, buttonSize;
            GetCloseButtonRect(out buttonTopLeft, out buttonSize);

            _closeHot = PortGeometry.Contains(buttonTopLeft, buttonSize, mouse);

            if (!_closeHot || !MyAPIGateway.Input.IsNewLeftMousePressed()) return false;

            Close();
            return true;
        }

        /// <summary>
        /// Pans the canvas while <paramref name="buttonDown"/> is held over it. Returns true when
        /// this frame's pointer input belongs to the pan, so the caller must not also hover,
        /// select or click.
        ///
        /// One implementation for the three places that pan: the idle canvas, the bind gesture
        /// and the watch gesture. The gestures pan with the middle button only — their right
        /// button already means "cancel" — while the idle canvas pans with either, which is why
        /// the button state is a parameter rather than read here.
        ///
        /// The anchor survives a pointer that leaves the canvas while the button is held: the pan
        /// resumes from where the drag left it instead of jumping. Dropping it on every frame
        /// outside the canvas would make a drag that grazes the header twitch.
        /// </summary>
        private bool PanWhileHeld(bool buttonDown, Vector2 mouse, Vector2 canvasTopLeft,
                                  Vector2 canvasSize)
        {
            if (!buttonDown)
            {
                _panning = false;
                return false;
            }

            if (!PortGeometry.Contains(canvasTopLeft, canvasSize, mouse)) return false;

            if (!_panning)
            {
                _panning = true;
                _panAnchor = mouse;
                return true;
            }

            _view.Camera -= (mouse - _panAnchor) / _view.Zoom;
            _panAnchor = mouse;
            return true;
        }

        /// <summary>
        /// Middle-button pan, shared by the bind and the watch gestures: both keep the middle
        /// button free for panning while the pointer is busy picking a target.
        /// </summary>
        private bool PanWithMiddleButton(Vector2 mouse, Vector2 canvasTopLeft, Vector2 canvasSize)
        {
            return PanWhileHeld(MyAPIGateway.Input.IsMiddleMousePressed(),
                                mouse, canvasTopLeft, canvasSize);
        }

        private void HandleCanvasInput(Vector2 mouse)
        {
            Vector2 canvasTopLeft, canvasSize;
            GetCanvasRect(out canvasTopLeft, out canvasSize);

            bool insideCanvas = PortGeometry.Contains(canvasTopLeft, canvasSize, mouse);

            // ---- zoom ----
            if (insideCanvas)
            {
                int wheel = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
                if (wheel != 0)
                {
                    // Zoom about the cursor so the point under the pointer stays put.
                    Vector2 graphUnderCursor = ScreenToGraph(mouse, canvasTopLeft);
                    float factor = wheel > 0 ? VgsConstants.ZoomStep : 1f / VgsConstants.ZoomStep;
                    float newZoom = _view.Zoom * factor;
                    if (newZoom < VgsConstants.MinZoom) newZoom = VgsConstants.MinZoom;
                    if (newZoom > VgsConstants.MaxZoom) newZoom = VgsConstants.MaxZoom;

                    if (newZoom != _view.Zoom)
                    {
                        _view.Zoom = newZoom;
                        _view.Camera = graphUnderCursor - (mouse - canvasTopLeft) / _view.Zoom;
                    }
                }
            }

            bool leftDown = MyAPIGateway.Input.IsLeftMousePressed();
            bool leftNew = MyAPIGateway.Input.IsNewLeftMousePressed();
            bool leftReleased = MyAPIGateway.Input.IsNewLeftMouseReleased();
            bool rightDown = MyAPIGateway.Input.IsRightMousePressed();
            bool middleDown = MyAPIGateway.Input.IsMiddleMousePressed();

            // ---- pan (right or middle button) ----
            if (PanWhileHeld(rightDown || middleDown, mouse, canvasTopLeft, canvasSize)) return;

            // ---- hover: the remove button, the card's button row and the card ----
            UpdateHover(mouse, canvasTopLeft, canvasSize);

            if (leftNew && insideCanvas)
            {
                Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);

                // The remove button is the smallest target on the canvas and it sits on a bar
                // that otherwise belongs to the card, so it is resolved before anything else:
                // a click meant to delete a connection must not select or drag the block.
                Port removePort;
                Node removeNode = HitTestRemoveButton(graphPos, out removePort);
                if (removeNode != null)
                {
                    RemovePortConnections(removeNode, removePort);
                    return;
                }

                // A watch port's "x" is the same kind of target on the same kind of bar, and it
                // is resolved before the card for the same reason (ADR-0017).
                WatchTarget watchRemove;
                Node watchRemoveNode = HitTestWatchRemoveButton(graphPos, out watchRemove);
                if (watchRemoveNode != null)
                {
                    RemoveWatchTarget(watchRemoveNode, watchRemove);
                    return;
                }

                // The card's button row is resolved before the card itself: every button sits on
                // the card, and starting a gesture instead of a drag is the whole point of it
                // (ADR-0020). Which button it was decides what happens — the jump leaves the
                // schema for the block's own terminal (ADR-0021), a bell starts a watch, a
                // lightbulb or a plus starts a bind.
                CardButtonKind button;
                Node buttonNode = HitTestCardButton(graphPos, out button);
                if (buttonNode != null)
                {
                    PortGroup group;
                    switch (button)
                    {
                        case CardButtonKind.Jump:
                            TryJumpToTerminal(buttonNode);
                            break;

                        case CardButtonKind.Watch:
                            BeginWatch(buttonNode);
                            break;

                        default:
                            // Everything else is a bind, and the switch above names the two kinds
                            // that are not: a new kind of button added to the enum lands here as
                            // a bind, which is the safe default — it starts a gesture the player
                            // can cancel rather than doing something irreversible.
                            if (PortGeometry.TryGroupOf(button, out group))
                            {
                                BeginBind(buttonNode, group);
                            }
                            break;
                    }
                    return;
                }

                Node hit = HitTest(graphPos);
                _selected = hit;

                // Only one drag can be in progress: a card drag and a group drag write to
                // different things (one position versus one offset on every member), and both
                // running at once would move the same card twice per frame. Clearing the other
                // field here is cheaper than defending every use site against a stale one.
                _draggedNode = null;
                _draggedGroup = null;

                // Two kinds of drag, and which one starts is decided by what was grabbed:
                //
                //  * a card is dragged by its body, and the node's own position follows the
                //    pointer;
                //  * a group is dragged by its title strip only (ADR-0016). The interior of the
                //    frame belongs to the member cards and the gaps between them stay canvas, so
                //    a click there still selects nothing and pans — a frame that swallowed those
                //    clicks would make the cluster impossible to pan across.
                if (hit != null && hit.IsGroup)
                {
                    if (PortGeometry.HitGroupTitle(hit, graphPos))
                    {
                        _draggedGroup = hit;
                        _groupDragAnchor = graphPos;
                        _groupDragLast = graphPos;
                        _dragging = false;
                    }
                }
                else if (hit != null)
                {
                    _draggedNode = hit;
                    _dragGrabOffset = graphPos - hit.Position;
                    _dragging = false;
                }
            }

            if (leftDown && _draggedNode != null)
            {
                Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);
                Vector2 target = graphPos - _dragGrabOffset;

                if (!_dragging)
                {
                    if ((target - _draggedNode.Position).Length() > VgsConstants.DragThreshold) _dragging = true;
                }

                if (_dragging)
                {
                    _draggedNode.Position = target;

                    // The frames of the groups this card belongs to follow it. Recomputed here,
                    // on the pointer move that changed the position, and not in the draw path:
                    // the same reason the graph itself is never read while drawing. Only the
                    // groups containing this card can have changed, so a drag costs a scan of
                    // the group list and one frame per group the card is in.
                    if (_graph.GroupCount > 0) PortGeometry.LayoutFramesContaining(_graph, _draggedNode);
                }
            }

            if (leftDown && _draggedGroup != null)
            {
                Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);

                // The threshold is measured from where the strip was grabbed, not from the
                // previous frame: a slow drag would otherwise never accumulate enough movement
                // to start.
                if (!_dragging && (graphPos - _groupDragAnchor).Length() > VgsConstants.DragThreshold)
                {
                    _dragging = true;
                }

                if (_dragging)
                {
                    Vector2 delta = graphPos - _groupDragLast;
                    _groupDragLast = graphPos;

                    // One offset for the whole cluster: the members keep their positions
                    // relative to each other, which is the entire point of dragging a group
                    // rather than its cards one by one.
                    if (_graph.OffsetGroupMembers(_draggedGroup, delta) > 0)
                    {
                        // Every frame is derived from member cards, so all of them are
                        // recomputed: a card may belong to more than one group, and the frames
                        // of the others have to follow it too. Bounded by the group count and
                        // only while the pointer is actually moving.
                        PortGeometry.LayoutGroupFrames(_graph);
                    }
                }
            }

            if (leftReleased)
            {
                // Where the user dropped the card is written to the block here, on release.
                // Not per pointer move (sixty writes a second), and not in OnClosed: closing
                // can be a session unload, which never reaches OnClosed at all, and that is
                // exactly when a player would notice the layout was lost (ADR-0012).
                if (_dragging && _draggedNode != null) NodeStateStore.SavePosition(_draggedNode);
                if (_dragging && _draggedGroup != null) SaveGroupPositions(_draggedGroup);

                _draggedNode = null;
                _draggedGroup = null;
                _dragging = false;
            }
        }

        /// <summary>
        /// Writes the new position of every member card of a dragged group to its block
        /// (ADR-0012, ADR-0016).
        ///
        /// A group has nowhere to store a position: it is not an entity and has no mod storage.
        /// The group's layout *is* its members' layouts, so saving the group means saving each
        /// member — and that is also what makes a dragged group survive a reload exactly like a
        /// dragged card.
        ///
        /// Members whose block disappeared mid-drag are skipped: <see cref="NodeStateStore.SavePosition"/>
        /// reports that as false, which is a normal outcome, not an error.
        /// </summary>
        private void SaveGroupPositions(Node group)
        {
            if (group == null) return;

            int saved = 0;
            for (int i = 0; i < group.Members.Count; i++)
            {
                Node member = _graph.GetNode(group.Members[i]);
                if (member == null || member.IsGroup) continue;
                if (NodeStateStore.SavePosition(member)) saved++;
            }

            VgsLog.Info("group '" + group.GroupName + "' moved: " + saved + " card position(s) saved");
        }

        /// <summary>
        /// Input while a bind is in progress: the pointer picks the target instead of dragging
        /// or panning. The right button cancels — which is why the right button cannot pan
        /// here; the middle button still does.
        /// </summary>
        private void HandleBindInput(Vector2 mouse)
        {
            bool rightNew = MyAPIGateway.Input.IsNewRightMousePressed();
            if (rightNew)
            {
                CancelBind();
                return;
            }

            Vector2 canvasTopLeft, canvasSize;
            GetCanvasRect(out canvasTopLeft, out canvasSize);
            bool insideCanvas = PortGeometry.Contains(canvasTopLeft, canvasSize, mouse);

            // Middle-button pan keeps working so the user can reach a target off-screen
            // without losing the bind.
            if (PanWithMiddleButton(mouse, canvasTopLeft, canvasSize)) return;

            Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);

            // A pointer over the header or the bottom bar is not over a card, even if a card
            // happens to be scrolled underneath: the bars are chrome, and the click there is
            // a click on chrome.
            //
            // The port controls are idle-mode targets only: while binding, every click picks the
            // target, and a bar (with its button) counts as its block. Dropping those highlights
            // is what stops a disc from looking clickable in a mode where it is not.
            ClearHoverTargets();
            _hoverNode = insideCanvas ? HitTest(graphPos) : null;

            if (!MyAPIGateway.Input.IsNewLeftMousePressed()) return;

            Node target = _hoverNode;
            if (target == null)
            {
                // Clicking the void abandons the bind, the same way a rubber band in any
                // editor is dropped by clicking on nothing.
                CancelBind();
                return;
            }

            if (ReferenceEquals(target, _bindSource))
            {
                VgsLog.Debug("bind rejected: source and target are the same block");
                return;
            }

            OpenActionMenu(target, mouse);
        }

        // ------------------------------------------------------------------ watch mode

        /// <summary>
        /// Input while a watch is being attached to a controller (ADR-0017).
        ///
        /// The gesture is the bind's twin and deliberately so: a line follows the pointer from
        /// the controller's bell, the left button picks the block, the right button and Esc
        /// cancel, the middle button still pans so a target off-screen can be reached without
        /// losing the gesture. What differs is what a click *means*: no action menu opens,
        /// because there is no action to choose — the controller only observes.
        /// </summary>
        private void HandleWatchInput(Vector2 mouse)
        {
            if (MyAPIGateway.Input.IsNewRightMousePressed())
            {
                CancelWatch();
                return;
            }

            Vector2 canvasTopLeft, canvasSize;
            GetCanvasRect(out canvasTopLeft, out canvasSize);
            bool insideCanvas = PortGeometry.Contains(canvasTopLeft, canvasSize, mouse);

            if (PanWithMiddleButton(mouse, canvasTopLeft, canvasSize)) return;

            Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);

            // The port controls are idle-mode targets only: while a watch is being attached,
            // every click picks a block, and a bar counts as its block.
            ClearHoverTargets();
            _hoverNode = insideCanvas ? HitTest(graphPos) : null;

            if (!MyAPIGateway.Input.IsNewLeftMousePressed()) return;

            Node target = _hoverNode;
            if (target == null)
            {
                // Clicking the void abandons the gesture, exactly like a bind.
                CancelWatch();
                return;
            }

            TryAttachWatch(target);
        }

        /// <summary>
        /// Starts watch mode from a controller's watch button. Refuses on a node that is not a
        /// controller, and on one whose list cannot be written from this machine — telling the
        /// player before they aim at a block rather than after.
        /// </summary>
        private void BeginWatch(Node controller)
        {
            if (controller == null || !controller.IsEventController) return;

            _watchSource = controller;
            _watchErrorUntilFrame = 0;
            _selected = controller;
            _dragging = false;
            _draggedNode = null;
            _draggedGroup = null;
            CancelBind();

            VgsLog.Debug("watch mode started on controller " + controller.EntityId);
        }

        /// <summary>
        /// Leaves watch mode. Deliberately does not touch the selection or the camera: the
        /// player is looking at the controller they just edited.
        /// </summary>
        private void CancelWatch()
        {
            if (_watchSource != null) VgsLog.Debug("watch mode cancelled");

            _watchSource = null;
            ResetPointerOverlay();
        }

        /// <summary>
        /// Attaches one block to the controller being edited, or refuses and turns the line red.
        ///
        /// <b>Order: reserve in the graph, write the block, keep the picture only on success.</b>
        /// The block is the record — the game's selection plus the overlay, never the graph
        /// (ADR-0018) — and the port is only its picture. The reservation is what makes a refusal
        /// cost nothing: it answers the cheap questions first (is this a controller, is the block
        /// already watched, is the stack full) and rolls back without a trace when the world
        /// refuses. `LayoutPorts` is not called between the two, so no port is ever drawn for a
        /// watch that did not happen.
        /// </summary>
        private void TryAttachWatch(Node target)
        {
            Node controller = _watchSource;
            if (controller == null || target == null) return;

            // A group is not an entity, so it has no id to store and nothing the controller
            // could subscribe to (ADR-0015, ADR-0017). Refused explicitly: falling through to
            // "the block is gone" would be a lie about a block the player can see.
            if (target.IsGroup)
            {
                RefuseWatch(VgsText.WarnWatchGroup);
                return;
            }

            IMyTerminalBlock controllerBlock = ResolveBlock(controller.EntityId);
            if (controllerBlock == null)
            {
                RefuseWatch(VgsText.WarnWatchBlockGone);
                return;
            }

            IMyTerminalBlock targetBlock = ResolveBlock(target.EntityId);
            if (targetBlock == null)
            {
                RefuseWatch(VgsText.WarnWatchBlockGone);
                return;
            }

            if (ReferenceEquals(target, controller))
            {
                RefuseWatch(VgsText.WarnWatchSelf);
                return;
            }

            if (_graph.HasWatch(controller, target.EntityId))
            {
                RefuseWatch(string.Format(VgsText.WarnWatchDuplicateFormat, target.DisplayName));
                return;
            }

            // The stack is capped, and the cap is enforced before the write so the refusal costs
            // nothing. A bar with no drawing slot would have no "x", and that button is the only
            // way to detach a block.
            if (controller.Controller != null && controller.Controller.IsFull)
            {
                RefuseWatch(string.Format(VgsText.WarnWatchTooManyFormat, GridGraph.MaxPortsPerSide));
                return;
            }

            // The graph is the reservation: it answers the cheap questions (is this a
            // controller, is the block already watched) before a single read of the world.
            if (!_graph.TryAddWatch(controller, target.EntityId, target.DisplayName))
            {
                RefuseWatch(VgsText.WarnWatchRejected);
                return;
            }

            EventControllerProbe.WatchResult result =
                EventControllerProbe.TryAddWatch(controllerBlock, targetBlock);

            if (result != EventControllerProbe.WatchResult.Ok)
            {
                _graph.RemoveWatch(controller, target.EntityId);
                RefuseWatch(DescribeWatchFailure(result, controllerBlock, target));
                return;
            }

            _graph.LayoutPorts();
            if (_graph.GroupCount > 0) PortGeometry.LayoutGroupFrames(_graph);

            VgsLog.Info("controller " + controller.EntityId + " now watches " + target.EntityId);
        }

        /// <summary>
        /// Removes a block from a controller's watch list from the port's "x".
        ///
        /// <b>Order: block first, graph second</b> — the mirror of attaching, and for the same
        /// reason: the block is the record, the port is its picture (ADR-0018). A refused removal
        /// keeps the port and says why, because dropping the bar while the block stayed watched
        /// would hide a live watch — and a block the player selected in the vanilla terminal
        /// cannot be removed from here at all, so it answers with that instead.
        /// </summary>
        private void RemoveWatchTarget(Node controller, WatchTarget watch)
        {
            if (controller == null || watch == null) return;

            IMyTerminalBlock controllerBlock = ResolveBlock(controller.EntityId);
            if (controllerBlock == null)
            {
                Warn(VgsText.WarnWatchBlockGone);
                return;
            }

            IMyTerminalBlock targetBlock = ResolveBlock(watch.EntityId);

            // A block that no longer exists has nothing left to unsubscribe from: the entry is
            // dropped from the storage and the bar goes with it. Refusing here would leave the
            // player with a port they can never remove.
            EventControllerProbe.WatchResult result;
            if (targetBlock == null)
            {
                result = EventControllerProbe.ForgetWatch(controllerBlock, watch.EntityId)
                    ? EventControllerProbe.WatchResult.Ok
                    : EventControllerProbe.WatchResult.Rejected;
            }
            else
            {
                result = EventControllerProbe.TryRemoveWatch(controllerBlock, targetBlock);
            }

            if (result != EventControllerProbe.WatchResult.Ok)
            {
                // A client is not a refusal of the game's — it is a different machine. Saying
                // "the game did not accept it" there would be untrue and would send the player
                // looking for a problem that does not exist.
                if (result == EventControllerProbe.WatchResult.ClientNotServer)
                {
                    Warn(VgsText.WarnWatchClient);
                    return;
                }

                // A block the player selected in the vanilla terminal is not ours to remove: the
                // controller's own list has no public setter, so the port would return on the
                // next refresh anyway (ADR-0018). Sending them to the terminal is the truth.
                if (result == EventControllerProbe.WatchResult.GameOwned)
                {
                    Warn(string.Format(VgsText.WarnWatchGameOwnedFormat, watch.DisplayName));
                    return;
                }

                Warn(string.Format(VgsText.WarnWatchRemoveRejectedFormat, watch.DisplayName));
                return;
            }

            _graph.RemoveWatch(controller, watch.EntityId);
            _hoverWatchRemoveNode = null;
            _hoverWatchRemoveTarget = null;

            _graph.LayoutPorts();
            if (_graph.GroupCount > 0) PortGeometry.LayoutGroupFrames(_graph);

            VgsLog.Info("controller " + controller.EntityId + " no longer watches " + watch.EntityId);
        }

        /// <summary>
        /// Reports a refused attach: the line turns red for
        /// <see cref="VgsConstants.WatchErrorFrames"/>, the reason goes to the bottom bar, and
        /// watch mode stays on so the next click can land on a block that fits.
        /// </summary>
        private void RefuseWatch(string reason)
        {
            _watchErrorUntilFrame = _screenFrame + VgsConstants.WatchErrorFrames;
            Warn(reason);
        }

        /// <summary>
        /// Turns a refusal into the sentence the player sees. Each reason has its own wording
        /// because they call for different actions: a different block, a different event, or a
        /// different machine.
        /// </summary>
        private string DescribeWatchFailure(EventControllerProbe.WatchResult result,
                                            IMyTerminalBlock controllerBlock, Node target)
        {
            switch (result)
            {
                case EventControllerProbe.WatchResult.IsSelf:
                    return VgsText.WarnWatchSelf;
                case EventControllerProbe.WatchResult.Duplicate:
                    return string.Format(VgsText.WarnWatchDuplicateFormat, target.DisplayName);
                case EventControllerProbe.WatchResult.NoEvent:
                    return VgsText.WarnWatchNoEvent;
                case EventControllerProbe.WatchResult.EventIgnoresBlocks:
                    return VgsText.WarnWatchEventIgnoresBlocks;
                case EventControllerProbe.WatchResult.DifferentConstruct:
                    return VgsText.WarnWatchDifferentConstruct;
                case EventControllerProbe.WatchResult.NotAccepted:
                    return string.Format(VgsText.WarnWatchNotAcceptedFormat, target.DisplayName,
                                         EventControllerProbe.SelectedEventName(controllerBlock));
                case EventControllerProbe.WatchResult.ClientNotServer:
                    return VgsText.WarnWatchClient;
                case EventControllerProbe.WatchResult.BlockGone:
                    return VgsText.WarnWatchBlockGone;
                default:
                    return VgsText.WarnWatchRejected;
            }
        }

        /// <summary>Input while the action menu is open.</summary>
        private void HandleMenuInput(Vector2 mouse)
        {
            if (MyAPIGateway.Input.IsNewRightMousePressed())
            {
                CancelBind();
                return;
            }

            int hoverRow = MenuRowAt(mouse);
            _menuHoverRow = hoverRow;

            int wheel = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
            if (wheel != 0) ScrollMenu(wheel > 0 ? -1 : 1);

            if (!MyAPIGateway.Input.IsNewLeftMousePressed()) return;

            Vector2 menuTopLeft, menuSize;
            GetMenuRect(out menuTopLeft, out menuSize);
            bool insideMenu = PortGeometry.Contains(menuTopLeft, menuSize, mouse);

            if (!insideMenu)
            {
                // A click next to the menu is a miss, not a decision: close the menu but keep
                // the bind, so a near-miss does not cost the user the source they picked.
                CloseActionMenu();
                return;
            }

            if (_menuRows.Count == 0)
            {
                // The target offers no actions at all, so this bind can never complete. Say so
                // and drop the whole gesture rather than leaving an unclickable menu on screen.
                VgsLog.Debug("bind cancelled: target has no terminal actions");
                CancelBind();
                return;
            }

            if (hoverRow < 0) return;   // title strip

            Port action = _menuRows[hoverRow];
            if (TryCompleteBind(action))
            {
                CloseActionMenu();
                CancelBind();
            }
        }

        // ------------------------------------------------------------------ jump to the block

        /// <summary>
        /// Leaves the schema for the block's own terminal (ADR-0021): switches the game's terminal
        /// to this block and closes the editor.
        ///
        /// <b>Close only on success.</b> This is the one card action that destroys the screen the
        /// player is looking at, so a refusal — the block was destroyed since the last refresh, or
        /// there is no local character to open a terminal for — has to leave the schema where it
        /// was and say why in the bottom bar. Closing first and reporting afterwards would cost
        /// the player their place over a block that no longer exists.
        ///
        /// <b>Switch the terminal before closing.</b> Closing is a transition that finishes over
        /// the next frames, while the terminal's tab rebuild is immediate; in this order the
        /// terminal already shows the right block by the time the closing screen uncovers it.
        ///
        /// A group is refused rather than attempted: it has no entity, so there is no terminal it
        /// could stand for. The button is not drawn on a group's frame either
        /// (<see cref="PortGeometry.HasCardButton"/>), so this is only a guard.
        /// </summary>
        private void TryJumpToTerminal(Node node)
        {
            if (node == null || node.IsGroup) return;

            string failure;
            if (!TerminalJump.TryOpen(node.EntityId, out failure))
            {
                // Warn both shows the sentence and writes it to the log — one line per event.
                Warn(failure ?? VgsText.WarnJumpFailed);
                return;
            }

            VgsLog.Info("terminal jump to block " + node.EntityId + ", closing the editor");

            // Closing is a separate call into the game, and the terminal has already been switched
            // by now. A throw here would reach the caller's throttled handler and read as "input
            // handling failed", which is not what happened — so it is caught here, where the state
            // is known: the jump succeeded, only the close did not, and the player can press Esc.
            try
            {
                Close();
            }
            catch (Exception e)
            {
                VgsLog.Error("terminal jump succeeded but the editor could not close", e);
            }
        }

        // ------------------------------------------------------------------ bind mode

        private void BeginBind(Node source, PortGroup group)
        {
            if (source == null) return;

            Port port = source.PrimaryEventFor(group);
            if (port == null)
            {
                VgsLog.Debug("bind not started: block has no port for group " + group);
                return;
            }

            _bindSource = source;
            _bindPort = port;
            _bindGroup = group;
            _selected = source;
            _dragging = false;
            _draggedNode = null;
            _panning = false;
            CloseActionMenu();

            VgsLog.Debug("bind started from " + source.EntityId + " via " + port.Key);
        }

        private void CancelBind()
        {
            if (_bindSource != null) VgsLog.Debug("bind cancelled");
            _bindSource = null;
            _bindPort = null;
            ResetPointerOverlay();
            CloseActionMenu();
        }

        private void OpenActionMenu(Node target, Vector2 mouse)
        {
            if (target == null) return;

            _menuTarget = target;
            _menuAnchorScreen = mouse;
            _menuScroll = 0;
            _menuHoverRow = -1;

            // The target card keeps the hover outline while its menu is open, so it is obvious
            // which of several similar blocks the list belongs to.
            ClearHoverTargets();
            _hoverNode = target;

            _menuRows.Clear();
            _menuLabels.Clear();

            FillMenuRows(target);

            // Formatted once, here, because DrawActionMenu runs every frame and string.Format
            // in a draw path is an allocation per frame for as long as the menu is open.
            _menuTitle = string.Format(VgsText.ActionMenuTitleFormat, target.DisplayName);

            VgsLog.Debug("action menu for " + target.EntityId + ": " + _menuRows.Count + " action(s)");
        }

        /// <summary>
        /// Fills the menu rows and their captions for one target.
        ///
        /// Called when the menu opens and again on every grid refresh while it stays open,
        /// because the captions carry the current fan-in count. Both call sites are outside the
        /// draw path on purpose: <see cref="DrawActionMenu"/> must only read these lists.
        /// </summary>
        private void FillMenuRows(Node target)
        {
            _menuRows.Clear();
            _menuLabels.Clear();
            if (target == null) return;

            for (int i = 0; i < target.Actions.Count; i++)
            {
                Port action = target.Actions[i];
                _menuRows.Add(action);

                // Input ports are shared, so an action that already has sources is still a
                // valid choice — the row says how many lines are already there instead of
                // being disabled. A disabled row would also make the menu jump as soon as the
                // user connected something.
                int fanIn = _graph.CountEdgesToInput(target, action.Key);
                _menuLabels.Add(fanIn > 0
                    ? action.DisplayName + string.Format(VgsText.ActionMenuFanInFormat, fanIn)
                    : action.DisplayName);
            }

            // The row count can shrink under an open menu; the scroll offset must not point
            // past the end, or the last rows become unreachable.
            int maxScroll = _menuRows.Count - VgsConstants.ActionMenuMaxRows;
            if (maxScroll < 0) maxScroll = 0;
            if (_menuScroll > maxScroll) _menuScroll = maxScroll;
        }

        private void CloseActionMenu()
        {
            _menuTarget = null;
            _menuRows.Clear();
            _menuLabels.Clear();
            _menuTitle = null;
            _menuHoverRow = -1;
            _menuScroll = 0;
        }

        private void ScrollMenu(int delta)
        {
            int maxScroll = _menuRows.Count - VgsConstants.ActionMenuMaxRows;
            if (maxScroll < 0) maxScroll = 0;

            _menuScroll += delta;
            if (_menuScroll > maxScroll) _menuScroll = maxScroll;
            if (_menuScroll < 0) _menuScroll = 0;
        }

        /// <summary>
        /// Index of the action row under the pointer, or -1. Returns an index into
        /// <see cref="_menuRows"/> (already offset by the scroll position).
        /// </summary>
        private int MenuRowAt(Vector2 mouse)
        {
            if (_menuRows.Count == 0) return -1;

            Vector2 menuTopLeft, menuSize;
            GetMenuRect(out menuTopLeft, out menuSize);
            if (!PortGeometry.Contains(menuTopLeft, menuSize, mouse)) return -1;

            float local = mouse.Y - menuTopLeft.Y - VgsConstants.ActionMenuTitleHeight;
            if (local < 0f) return -1;

            int row = (int)(local / VgsConstants.ActionMenuRowHeight);
            if (row < 0 || row >= VisibleMenuRows) return -1;

            int index = row + _menuScroll;
            return index < _menuRows.Count ? index : -1;
        }

        /// <summary>
        /// Creates the connection the bind was aiming at.
        ///
        /// The order is deliberate: the item goes into the source block's real toolbar first,
        /// and only then into the graph. The toolbar is what makes the connection work
        /// (ADR-0011), so a graph edge without an item would be a line that does nothing. If
        /// the game refuses the placement — every page already holds that action, or the page
        /// list is full — the user is told why and nothing is changed.
        ///
        /// A group target is written as a group item, not as one item per member: that is the
        /// whole point of a group in this editor (ADR-0015). One cell, one item, and the game
        /// applies the action to every functional member when it fires. The two kinds share
        /// every step except the item itself, so they are branches of one method rather than
        /// two methods that would drift apart.
        ///
        /// Returns true only when the block really holds the connection afterwards. A click
        /// that could not be satisfied returns false, so the menu stays open and the user can
        /// pick another action instead of wondering whether the click registered.
        /// </summary>
        private bool TryCompleteBind(Port action)
        {
            Node source = _bindSource;
            Node target = _menuTarget;
            Port port = _bindPort;
            if (source == null || target == null || action == null || port == null) return false;

            IMyTerminalBlock sourceBlock = ResolveBlock(source.EntityId);
            if (sourceBlock == null)
            {
                Warn(VgsText.WarnBlockGone);
                return false;
            }

            ToolbarCell cell;
            ToolbarWriter.WriteResult result;
            bool toGroup = target.IsGroup;

            if (toGroup)
            {
                result = ToolbarWriter.TryWriteGroup(sourceBlock, source, port, target.GroupName,
                                                     action.Key, out cell);
            }
            else
            {
                IMyTerminalBlock targetBlock = ResolveBlock(target.EntityId);
                if (targetBlock == null)
                {
                    Warn(VgsText.WarnBlockGone);
                    return false;
                }

                result = ToolbarWriter.TryWrite(sourceBlock, source, port, targetBlock, action.Key, out cell);
            }

            if (result != ToolbarWriter.WriteResult.Ok)
            {
                Warn(DescribeWriteFailure(result, source, target, action));
                return false;
            }

            bool connected = toGroup
                ? _graph.TryConnectToGroup(source.EntityId, port.Key, target.GroupName, action.Key, cell)
                : _graph.TryConnect(source.EntityId, port.Key, target.EntityId, action.Key, cell);

            if (!connected)
            {
                // The graph refused an item the block now holds: put the cell back the way it
                // was rather than leaving wiring the editor does not show.
                VgsLog.Warn("bind rejected by graph: " + port.Key + " -> " + action.Key + " @ " + cell);
                ToolbarWriter.TryClear(sourceBlock, source, cell);
                Warn(VgsText.WarnRejected);
                return false;
            }

            // The new connection changes which port bars exist, so the stacks are recomputed.
            // A group's frame has to grow with its bars, so the frames follow.
            _graph.LayoutPorts();
            if (_graph.GroupCount > 0) PortGeometry.LayoutGroupFrames(_graph);

            VgsLog.Info("connected " + source.EntityId + " (" + port.Key + ") -> "
                        + (toGroup ? "group:" + target.GroupName : target.EntityId.ToString())
                        + " (" + action.Key + ") @ " + cell);
            return true;
        }

        /// <summary>
        /// Turns a write failure into the sentence the user sees. Each reason has its own
        /// wording because they call for different actions: a full toolbar needs a page freed,
        /// a duplicate needs a different target action, a vanished action needs a refresh.
        /// </summary>
        private static string DescribeWriteFailure(ToolbarWriter.WriteResult result, Node source,
                                                   Node target, Port action)
        {
            switch (result)
            {
                case ToolbarWriter.WriteResult.NotDrivable:
                    return string.Format(VgsText.WarnNotDrivableFormat, source.DisplayName);
                case ToolbarWriter.WriteResult.ActionOnEveryPage:
                    return string.Format(VgsText.WarnEveryPageFormat, action.DisplayName, source.DisplayName);
                case ToolbarWriter.WriteResult.NoFreeCell:
                    return string.Format(VgsText.WarnNoFreeCellFormat, source.DisplayName);
                case ToolbarWriter.WriteResult.ActionUnavailable:
                    return target.IsGroup
                        ? string.Format(VgsText.WarnGroupGoneFormat, target.GroupName)
                        : string.Format(VgsText.WarnActionGoneFormat, target.DisplayName, action.DisplayName);
                default:
                    return VgsText.WarnRejected;
            }
        }

        /// <summary>
        /// Removes every connection the given port carries, together with the items the source
        /// blocks hold in their toolbars.
        ///
        /// A port carries a set, not a single connection: an output may drive several targets
        /// (fan-out) and an input may be driven by several sources (fan-in). The button on the
        /// port therefore clears the whole set — that is what "delete this port's connections"
        /// means, and leaving some of them behind would keep the bar on screen after the click.
        ///
        /// <b>Order: toolbar first, graph second.</b> The toolbar is the connection (ADR-0011),
        /// and the refresh pass rebuilds edges from it. An edge dropped from the graph while its
        /// item stayed in the block would come back on the next refresh — the line would
        /// reappear under the user's cursor with no explanation. When the game refuses the clear
        /// (a remote client does not own the block's toolbar), that connection is kept and the
        /// user is told why, which is the same honesty the write path applies to a refused
        /// write.
        ///
        /// A destroyed block needs no clearing: its toolbar went with it, and the stale edge is
        /// dropped from the graph here.
        ///
        /// The other end of each connection needs no special handling:
        /// <see cref="GridGraph.LayoutPorts"/> gives a port a drawing slot only while it carries
        /// at least one edge, so a port that has just lost its last connection — the source's
        /// output or the target's input — disappears by itself.
        /// </summary>
        private void RemovePortConnections(Node node, Port port)
        {
            if (node == null || port == null) return;

            _removeBuffer.Clear();
            _graph.CollectEdgesForPort(node, port.Key, port.Direction, _removeBuffer);
            if (_removeBuffer.Count == 0) return;

            int removed = 0;
            int changedUnderEditor = 0;
            long warnedAbout = 0;

            for (int i = 0; i < _removeBuffer.Count; i++)
            {
                Edge edge = _removeBuffer[i];

                Node source = _graph.GetNode(edge.SourceEntityId);
                IMyTerminalBlock sourceBlock = ResolveBlock(edge.SourceEntityId);

                // Only a cell that really holds this connection needs clearing. An edge without a
                // valid cell cannot be expressed in a toolbar at all (TryConnect refuses one), so
                // it is dropped without a warning rather than kept forever as an undeletable line.
                if (edge.Cell.IsValid && sourceBlock != null && source != null)
                {
                    CellState state = ReadCellState(sourceBlock, edge);

                    // The cell no longer holds this connection: it is empty, or the player
                    // replaced it by hand in the terminal during the up-to-one-refresh window when
                    // the graph lags the world. Either way it is not ours to clear, so it is left
                    // alone; the reconcile pass then turns whatever is there now into its own edge.
                    if (state == CellState.HoldsOther)
                    {
                        changedUnderEditor++;
                        if (_graph.RemoveEdge(edge)) removed++;
                        continue;
                    }

                    // HoldsThis is the only state that may be cleared. Unreadable is NOT "safe to
                    // clear": the player's edit cannot be ruled out, so the connection is kept and
                    // the reason is shown — the same answer as a refused clear. Dropping the edge
                    // here would make the line reappear at the next reconcile pass with no
                    // explanation, which is the defect this whole method exists to avoid.
                    if (state != CellState.HoldsThis
                        || !ToolbarWriter.TryClear(sourceBlock, source, edge.Cell))
                    {
                        WarnRemoveRefused(source, ref warnedAbout);
                        continue;
                    }
                }

                if (_graph.RemoveEdge(edge)) removed++;
            }

            _removeBuffer.Clear();
            _hoverRemoveNode = null;
            _hoverRemovePort = null;

            // The stacks are a derived view of the edges: the port that just lost its last
            // connection must lose its bar, and the stack under it must close up.
            _graph.LayoutPorts();
            if (_graph.GroupCount > 0) PortGeometry.LayoutGroupFrames(_graph);

            if (changedUnderEditor > 0)
            {
                VgsLog.Info(changedUnderEditor + " cell(s) no longer held their connection and were "
                            + "left untouched; the reconcile pass re-reads them");
            }

            if (removed > 0)
            {
                VgsLog.Info("removed " + removed + " connection(s) at " + node.EntityId
                            + " (" + port.Key + ")");
            }
        }

        /// <summary>
        /// What a connection's toolbar cell currently holds, as far as the editor can tell.
        ///
        /// Three outcomes, and they must not be collapsed into a bool: the caller's answer to
        /// "holds this connection" is *clear the cell*, its answer to "holds something else" is
        /// *leave it alone*, and its answer to "cannot tell" is *do nothing and say so*. A bool
        /// would make the third indistinguishable from the second and the removal would silently
        /// do nothing (the line then reappears at the next reconcile pass).
        /// </summary>
        private enum CellState
        {
            /// <summary>The cell holds exactly this connection: safe to clear.</summary>
            HoldsThis = 0,

            /// <summary>
            /// The cell no longer holds this connection: it is empty, or it holds another item.
            /// Either way it is not ours to clear — an empty cell has nothing to clear, and the
            /// other item is the player's edit.
            /// </summary>
            HoldsOther = 1,

            /// <summary>The toolbar could not be read. Neither clearing nor dropping is safe.</summary>
            Unreadable = 2
        }

        /// <summary>
        /// Reads the cell this edge occupies and reports which of the three cases it is: the same
        /// target block and action, a different item, or nothing readable.
        ///
        /// A read, not a write, so it is safe on a remote client too — which is the point: the
        /// check is what stops a removal from destroying an edit the player made in the terminal
        /// during the up-to-one-refresh window when the graph is behind the world.
        /// </summary>
        private static CellState ReadCellState(IMyTerminalBlock sourceBlock, Edge edge)
        {
            try
            {
                IMyToolbar toolbar = ToolbarProbe.GetToolbar(sourceBlock, edge.Cell.ToolbarIndex);
                if (toolbar == null) return CellState.Unreadable;

                CellContents contents = ToolbarWriter.TryDescribe(toolbar, edge.Cell.Index);
                if (contents.IsEmpty) return CellState.HoldsOther;

                bool holdsThis = edge.TargetsGroup
                    ? contents.HoldsGroupAction(edge.TargetGroupName, edge.TargetPortKey)
                    : contents.HoldsBlockAction(edge.TargetEntityId, edge.TargetPortKey);

                return holdsThis ? CellState.HoldsThis : CellState.HoldsOther;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("cell-state", "could not read toolbar cell", e, 300);
                return CellState.Unreadable;
            }
        }

        /// <summary>
        /// Reports that a connection could not be removed from its block's toolbar, once per
        /// source block.
        ///
        /// Once, because a fan-in of three connections from the same block would otherwise queue
        /// the same sentence three times into one bottom bar, which shows only the last copy — the
        /// user would see one message and no indication that three attempts were made.
        /// <paramref name="warnedAbout"/> is the caller's running record, so the deduplication
        /// survives the loop without the caller keeping a set.
        /// </summary>
        private void WarnRemoveRefused(Node source, ref long warnedAbout)
        {
            if (source == null) return;
            if (warnedAbout == source.EntityId) return;

            warnedAbout = source.EntityId;
            Warn(string.Format(VgsText.WarnRemoveRefusedFormat, source.DisplayName));
        }

        /// <summary>Shows a failure on the bottom bar and records it in the log.</summary>
        private void Warn(string text)
        {
            _warningText = text;
            _warningUntilFrame = _screenFrame + VgsConstants.WarningFrames;
            VgsLog.Warn(text);
        }

        /// <summary>
        /// Resolves a node back to its live block. The entity id can be stale — the block may
        /// have been destroyed since the last refresh — so the result is checked, never assumed.
        ///
        /// Shorthand for the call sites below; the lookup itself is
        /// <see cref="EntityResolver.TryResolveTerminalBlock"/>, the one implementation of
        /// "entity id → block" in the mod (it carries the try/catch and the log tag).
        /// </summary>
        private static IMyTerminalBlock ResolveBlock(long entityId)
        {
            IMyTerminalBlock block;
            return EntityResolver.TryResolveTerminalBlock(entityId, out block) ? block : null;
        }

        // ------------------------------------------------------------------ hit testing

        /// <summary>Converts a normalized screen point to graph space.</summary>
        private Vector2 ScreenToGraph(Vector2 screenPoint, Vector2 canvasTopLeft)
        {
            return (screenPoint - canvasTopLeft) / _view.Zoom + _view.Camera;
        }

        /// <summary>Converts a graph-space point to normalized screen space.</summary>
        private Vector2 GraphToScreen(Vector2 graphPoint, Vector2 canvasTopLeft)
        {
            return (graphPoint - _view.Camera) * _view.Zoom + canvasTopLeft;
        }

        /// <summary>
        /// Topmost node under a graph-space point, or null. Ports count as the node.
        ///
        /// Blocks are tested before groups: the frame of a group surrounds its members, so a
        /// click on a card must select the card and not the group that contains it. Groups are
        /// only hit through their title strip and their port bars
        /// (<see cref="PortGeometry.HitNode"/>), which is what makes that ordering sufficient.
        /// </summary>
        private Node HitTest(Vector2 graphPoint)
        {
            IList<Node> nodes = _graph.Nodes;
            // Iterate backwards so the visually topmost (last drawn) node wins.
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                if (PortGeometry.HitNode(nodes[i], graphPoint)) return nodes[i];
            }

            IList<Node> groups = _graph.Groups;
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                if (PortGeometry.HitNode(groups[i], graphPoint)) return groups[i];
            }
            return null;
        }

        /// <summary>
        /// Topmost node whose card button row is under the pointer, and which of its buttons.
        ///
        /// One test for the whole row, because the row is one control group (ADR-0020): the jump
        /// button, the watch button, the yes/no pair and the single bind button differ only in
        /// what they do, so resolving them separately would mean five loops over the same nodes
        /// and an ordering question between them.
        /// </summary>
        private Node HitTestCardButton(Vector2 graphPoint, out CardButtonKind kind)
        {
            kind = CardButtonKind.Jump;
            float aspect = VgsScreenDraw.ClientAspect;
            IList<Node> nodes = _graph.Nodes;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                CardButtonKind hit;
                if (PortGeometry.HitCardButton(nodes[i], graphPoint, aspect, out hit))
                {
                    kind = hit;
                    return nodes[i];
                }
            }
            return null;
        }

        /// <summary>Topmost controller whose watch port carries the "x" under the pointer.</summary>
        private Node HitTestWatchRemoveButton(Vector2 graphPoint, out WatchTarget watch)
        {
            watch = null;
            float aspect = VgsScreenDraw.ClientAspect;

            IList<Node> nodes = _graph.Nodes;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                WatchTarget hit = PortGeometry.HitWatchRemoveButton(nodes[i], graphPoint, aspect);
                if (hit == null) continue;
                watch = hit;
                return nodes[i];
            }
            return null;
        }

        /// <summary>Topmost node whose port remove button is under the pointer, and which port.</summary>
        private Node HitTestRemoveButton(Vector2 graphPoint, out Port port)
        {
            port = null;
            float aspect = VgsScreenDraw.ClientAspect;

            // Blocks first, for the same reason as in HitTest: a group's frame contains its
            // members, and a member's own remove button is the more specific target.
            IList<Node> nodes = _graph.Nodes;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                Port hit = PortGeometry.HitPortRemoveButton(nodes[i], graphPoint, aspect);
                if (hit == null) continue;
                port = hit;
                return nodes[i];
            }

            IList<Node> groups = _graph.Groups;
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                Port hit = PortGeometry.HitPortRemoveButton(groups[i], graphPoint, aspect);
                if (hit == null) continue;
                port = hit;
                return groups[i];
            }
            return null;
        }

        /// <summary>
        /// Refreshes the hover targets used for the highlight of a card, of the buttons on it and
        /// of the remove button on one of its ports.
        ///
        /// Hover is recomputed from the pointer every input pass rather than cached until a
        /// move event: the canvas pans and zooms under a still pointer, so "under the pointer"
        /// changes without the pointer moving.
        /// </summary>
        private void UpdateHover(Vector2 mouse, Vector2 canvasTopLeft, Vector2 canvasSize)
        {
            ClearHoverTargets();

            if (!PortGeometry.Contains(canvasTopLeft, canvasSize, mouse)) return;

            Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);

            // The remove button is the smallest target on the canvas, and it is drawn on top of
            // a bar that belongs to the card. Resolving it first is what makes the highlight
            // land on the disc instead of on the whole port.
            Port removePort;
            Node removeNode = HitTestRemoveButton(graphPos, out removePort);
            if (removeNode != null)
            {
                _hoverRemoveNode = removeNode;
                _hoverRemovePort = removePort;
                return;
            }

            // A watch port's "x" is the same kind of target (ADR-0017).
            WatchTarget watchRemove;
            Node watchRemoveNode = HitTestWatchRemoveButton(graphPos, out watchRemove);
            if (watchRemoveNode != null)
            {
                _hoverWatchRemoveNode = watchRemoveNode;
                _hoverWatchRemoveTarget = watchRemove;
                return;
            }

            // The card's button row before the card: a button sits on the card, and a card
            // highlight under a button would suggest a drag where a click starts a gesture.
            CardButtonKind kind;
            _hoverButton = HitTestCardButton(graphPos, out kind);
            _hoverButtonKind = kind;

            // A card under a control is not "hovered": the control is the thing about to be
            // clicked, and highlighting the card as well would suggest a drag.
            _hoverNode = _hoverButton == null ? HitTest(graphPos) : null;
        }

    }
}
