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
    /// The argument dialog: one window that asks for every value an action needs, validates
    /// them, and either creates the connection or changes an existing one (ADR-0050).
    ///
    /// A partial of <see cref="VgsEditorScreen"/>. This file is the behaviour — opening,
    /// validating, writing, cancelling and the keyboard; the geometry and the drawing are in
    /// <c>VgsEditorScreen.ParameterDialog.cs</c>, so the rectangles the pointer is tested against
    /// and the rectangles that are painted cannot drift apart.
    ///
    /// <b>Why the editor asks at all.</b> Some actions do nothing without arguments — a
    /// programmable block's <c>Run</c> needs the string it passes to the script, a rotor's
    /// <c>RotateToAngle</c> needs an angle and a speed — and the game stores those values
    /// <em>inside the toolbar item</em>. A connection written without them would be a connection
    /// that runs the action the way a bare button would, which is not what the player drew.
    ///
    /// <b>Why a form and not one prompt per argument.</b> The game asks sequentially; that is
    /// exactly the part the product owner asked not to copy. All values are visible at once, the
    /// refusal names the field that is wrong, and Cancel is one decision rather than one per
    /// value.
    ///
    /// <b>Cancel means nothing happened.</b> In create mode no item is written and the bind is
    /// dropped; in edit mode the connection keeps the arguments it had. That is why the write
    /// happens on confirm and not when the dialog opens: an aborted dialog must not leave a
    /// half-made connection behind.
    ///
    /// <b>The game is not paused.</b> The screen never pauses the session (ADR-0029), and this
    /// dialog is part of the screen rather than a game screen, so the world keeps running while
    /// the player types — the same as every other gesture in the editor.
    /// </summary>
    public sealed partial class VgsEditorScreen
    {
        // ------------------------------------------------------------------ state

        /// <summary>The form being filled, or null when no dialog is open.</summary>
        private ParameterForm _paramForm;

        /// <summary>Whether the dialog creates a connection or changes one.</summary>
        private ParameterDialogMode _paramMode;

        /// <summary>The action the values belong to; its caption is the dialog's title.</summary>
        private Port _paramAction;

        /// <summary>The node the action belongs to — the bind's target, or the edited bar's owner.</summary>
        private Node _paramTarget;

        /// <summary>The bind's source, or null in edit mode.</summary>
        private Node _paramBindSource;

        /// <summary>The event port the pending bind started from, or null in edit mode.</summary>
        private Port _paramBindPort;

        /// <summary>The connection being edited, or null in create mode.</summary>
        private Edge _paramEdge;

        /// <summary>
        /// Which button the pointer is over: 0 none, 1 confirm, 2 cancel. An index rather than
        /// two flags, because the pair is one control and only one of them can be hot.
        /// </summary>
        private int _paramHotButton;

        /// <summary>Index of the field under the pointer, or -1.</summary>
        private int _paramHotField = -1;

        /// <summary>What the dialog is for. Two modes, two different writes, one form.</summary>
        private enum ParameterDialogMode
        {
            /// <summary>A bind is waiting for its arguments.</summary>
            Create = 0,

            /// <summary>An existing connection is being given new arguments.</summary>
            Edit = 1
        }

        /// <summary>True while the dialog owns the pointer and the keyboard.</summary>
        private bool ParameterDialogOpen
        {
            get { return _paramForm != null; }
        }

        // ------------------------------------------------------------------ opening

        /// <summary>
        /// Opens the dialog for a bind that cannot be completed without arguments.
        ///
        /// Called instead of the write, so nothing reaches the block until the player confirms.
        /// The bind stays armed while the dialog is open, which is what lets Cancel return the
        /// player to the schema with the source still chosen instead of making them start over.
        /// </summary>
        private void BeginCreateParameters(Node source, Port eventPort, Node target, Port action)
        {
            if (source == null || target == null || action == null || eventPort == null) return;

            _paramMode = ParameterDialogMode.Create;

            // The starting values come from the block the connection is being made to, because
            // that is where the useful ones live: the argument the script ran last, the speed the
            // rotor is turning at, the limits it is set to (ADR-0051). A group has no single
            // block, so its fields open empty and the player fills them in.
            ParameterSet defaults = ParameterSet.None;
            if (!target.IsGroup)
            {
                IMyTerminalBlock targetBlock = ResolveBlock(target.EntityId);
                if (targetBlock != null)
                {
                    defaults = ActionParameterDefaults.For(targetBlock, action.Key, action.ParameterTypes);
                }
            }

            _paramForm = new ParameterForm(action.Key, action.DisplayName, VgsText.Language,
                                              action.ParameterTypes, defaults);
            _paramAction = action;
            _paramTarget = target;
            _paramBindSource = source;
            _paramBindPort = eventPort;
            _paramEdge = null;
            _paramHotButton = 0;
            _paramHotField = -1;

            LabelParameterFields(action);
            InvalidateParameterHints();
            ReportCoverage(action);
            VgsLog.Info("parameters requested for action " + action.Key + " on "
                        + (target.IsGroup ? "group:" + target.GroupName : target.EntityId.ToString()));
        }

        /// <summary>
        /// Opens the dialog for an existing connection, pre-filled with what it already passes.
        ///
        /// <b>The bar, not the port, is the unit of editing.</b> Two requests of one action are
        /// two bars with two argument sets; editing one must not touch the other, so the dialog is
        /// opened from the bar and the edge is identified by the cell it occupies.
        /// </summary>
        private void BeginEditParameters(Node node, InputBar bar)
        {
            if (node == null || bar == null || bar.Port == null) return;

            Edge edge = FindEdgeForBar(node, bar);
            if (edge == null) return;

            _paramMode = ParameterDialogMode.Edit;
            _paramForm = new ParameterForm(bar.Port.Key, bar.Port.DisplayName, VgsText.Language,
                                              bar.Port.ParameterTypes, bar.Parameters);
            _paramAction = bar.Port;
            _paramTarget = node;
            _paramBindSource = null;
            _paramBindPort = null;
            _paramEdge = edge;
            _paramHotButton = 0;
            _paramHotField = -1;

            LabelParameterFields(bar.Port);
            InvalidateParameterHints();
            VgsLog.Info("parameters of " + bar.Port.Key + " opened for editing on "
                        + (node.IsGroup ? "group:" + node.GroupName : node.EntityId.ToString()));
        }

        /// <summary>
        /// The connection that produced one input bar: the first edge whose request the bar is.
        /// A bar may carry several edges — several events asking the same thing — and they all
        /// pass the same arguments, so any one of them identifies the cell to rewrite.
        /// </summary>
        private Edge FindEdgeForBar(Node node, InputBar bar)
        {
            _removeBuffer.Clear();
            _graph.CollectEdgesForBar(node, bar, _removeBuffer);

            Edge edge = _removeBuffer.Count > 0 ? _removeBuffer[0] : null;
            _removeBuffer.Clear();
            return edge;
        }

        /// <summary>
        /// Names the fields and tells the player what each one accepts.
        ///
        /// <b>Three levels of knowledge, in this order.</b> The action's own argument table
        /// (<see cref="ActionParameterHints"/>) gives the game's own caption — "Angle, °",
        /// "Speed", "Direction" — and, for a fixed list, the meaning of every allowed value
        /// ("0 — auto, 1 — clockwise"). Where the table has nothing, the field is named by the kind
        /// of value it takes and its number, which is what the game's metadata supports and no
        /// more. Either way the name comes from the text tables, so it is translated (ADR-0033).
        ///
        /// <b>Numbered only when there is more than one and the name is generic.</b> A single
        /// "Argument" needs no number; numbering a lone field suggests there are others.
        /// </summary>
        private void LabelParameterFields(Port action)
        {
            if (_paramForm == null || action == null) return;

            bool many = _paramForm.Count > 1;

            for (int i = 0; i < _paramForm.Count; i++)
            {
                ParameterSpec spec = _paramForm.SpecAt(i);
                string name = spec != null ? spec.Name : null;

                // <b>Every action the table describes arrives here already named</b> — the name is
                // the caption of the dialog the game builds for that argument, read from the
                // assemblies (ADR-0052 §2). This fallback therefore cannot fire for a built-in
                // action, and `offline-check.ps1` asserts exactly that for all 22 of them.
                //
                // It exists for the one case the scan cannot reach: an action added by another
                // mod, whose assembly is not the one the table was derived from. For such an
                // action with a single argument the game's own pattern makes the dialog's caption
                // the action's caption, so the action's name is the honest answer rather than a
                // made-up one. With several arguments there is no such pattern, and numbering is
                // the only truthful thing left to say.
                if (string.IsNullOrEmpty(name) && !many) name = _paramForm.ActionCaption;

                if (string.IsNullOrEmpty(name))
                {
                    string kind = DescribeParameterType(spec);
                    name = many
                        ? kind + " " + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : kind;
                }

                _paramForm.SetLabel(i, name);
                _paramForm.SetHint(i, DescribeParameterHint(spec));
            }
        }

        /// <summary>
        /// The line under a field: the allowed values of a fixed list, or the unit-free note that a
        /// field takes whole numbers. Null when there is nothing useful to say — an unconstrained
        /// number or a free-text field explains itself by its name.
        /// </summary>
        private static string DescribeParameterHint(ParameterSpec spec)
        {
            if (spec == null) return null;

            if (spec.IsChoice)
            {
                // "допустимо: 0 — авто, 1 — по часовой, 2 — против часовой". The values are the
                // ones the player types; the meanings are the game's own words for them.
                var parts = new System.Text.StringBuilder(48);
                for (int i = 0; i < spec.ChoiceCount; i++)
                {
                    string value = spec.ChoiceAt(i);
                    string meaning = VgsText.ParameterChoice(spec.ChoiceKeyAt(i));
                    if (value == null) continue;

                    if (parts.Length > 0) parts.Append(", ");
                    parts.Append(meaning == null
                        ? value
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                        VgsText.ParameterHintMeaningFormat, value, meaning));
                }

                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                     VgsText.ParameterHintChoiceFormat, parts.ToString());
            }

            if (spec.Slots > 1)
            {
                // One field, several values — the game asks for them in one dialog, so they are
                // typed in one field, separated by commas. Saying so is the difference between a
                // field the player can fill and a field that silently refuses what they type.
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                     VgsText.ParameterHintSeveralFormat, spec.Slots);
            }

            if (spec.Kind == ParameterKind.WholeNumber) return VgsText.ParameterHintWholeNumber;

            return null;
        }

        /// <summary>
        /// The player-facing name of an argument type, for the fields the argument table does not
        /// describe: the two kinds the requirement names — text and number — plus the switch and
        /// the fixed list, which the game also uses. Never a .NET type name, which would leak the
        /// implementation into the interface.
        /// </summary>
        private static string DescribeParameterType(ParameterSpec spec)
        {
            if (spec == null) return VgsText.ParameterKindText;

            switch (spec.Kind)
            {
                case ParameterKind.Choice: return VgsText.ParameterKindNumber;
                case ParameterKind.WholeNumber: return VgsText.ParameterKindNumber;
                case ParameterKind.Number: return VgsText.ParameterKindNumber;
                default:
                    return spec.Type == TypeCode.Boolean
                        ? VgsText.ParameterKindSwitch
                        : VgsText.ParameterKindText;
            }
        }

        /// <summary>
        /// Says, once per action, that this action's arguments are not described — the coverage
        /// report of ADR-0052 §4.
        ///
        /// <b>Why a log line and not a silent fallback.</b> The requirement asked which actions are
        /// not covered. A table nobody can audit answers that with a guess; a line per action id
        /// answers it with a fact the player (or I) can read out of the log after one session.
        /// Throttled per id, so a block that opens the dialog ten times reports once.
        /// </summary>
        private static void ReportCoverage(Port action)
        {
            if (action == null) return;
            if (ActionParameterHints.HasKnownTypes(action.Key)) return;

            // The message-only overload, not the one that takes an exception: there is no
            // exception here — an undescribed action is a fact, and a line ending in
            // ":: unknown" would read as a failure.
            VgsLog.Throttled("param-coverage:" + action.Key,
                             "action '" + action.Key + "' declares arguments this build does not"
                             + " describe; values are written with the declared type",
                             VgsConstants.ParameterCoverageLogInterval);
        }

        // ------------------------------------------------------------------ confirming

        /// <summary>
        /// Reads the form and, when it is valid, performs the write it was opened for.
        ///
        /// <b>Nothing is written until the form validates.</b> A refused value keeps the dialog
        /// open with the caret on the offending field: the requirement is explicit that an invalid
        /// value must not create the connection, and the cheapest way to honour that is to make
        /// the invalid state impossible to confirm.
        /// </summary>
        private void ConfirmParameters()
        {
            if (_paramForm == null) return;

            ParameterSet values;
            if (!_paramForm.TryBuild(out values))
            {
                Warn(DescribeParameterError(_paramForm.Error, _paramAction));
                return;
            }

            bool creating = _paramMode == ParameterDialogMode.Create;
            bool ok = creating ? WritePendingBind(values) : WriteEditedParameters(values);
            if (!ok) return;

            CloseParameterDialog();
            if (creating) CancelBind();
        }

        /// <summary>
        /// The bind's write, now that the arguments are known. Same order as the plain path —
        /// toolbar first, graph second — and the same read-back check, because a connection is
        /// only real once the block holds it (ADR-0011).
        /// </summary>
        private bool WritePendingBind(ParameterSet values)
        {
            Node source = _paramBindSource;
            Node target = _paramTarget;
            Port eventPort = _paramBindPort;
            Port action = _paramAction;
            if (source == null || target == null || eventPort == null || action == null) return false;

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
                result = ToolbarWriter.TryWriteGroup(sourceBlock, source, eventPort, target.GroupName,
                                                     action.Key, values, out cell);
            }
            else
            {
                IMyTerminalBlock targetBlock = ResolveBlock(target.EntityId);
                if (targetBlock == null)
                {
                    Warn(VgsText.WarnBlockGone);
                    return false;
                }

                result = ToolbarWriter.TryWrite(sourceBlock, source, eventPort, targetBlock,
                                                action.Key, values, out cell);
            }

            if (result != ToolbarWriter.WriteResult.Ok)
            {
                Warn(DescribeWriteFailure(result, source, target, action));
                return false;
            }

            bool connected = toGroup
                ? _graph.TryConnectToGroup(source.EntityId, eventPort.Key, target.GroupName,
                                           action.Key, cell, values)
                : _graph.TryConnect(source.EntityId, eventPort.Key, target.EntityId, action.Key,
                                    cell, values);

            if (!connected)
            {
                VgsLog.Warn("bind rejected by graph: " + eventPort.Key + " -> " + action.Key + " "
                            + values + " @ " + cell);
                ToolbarWriter.TryClear(sourceBlock, source, cell);
                Warn(VgsText.WarnRejected);
                return false;
            }

            _graph.LayoutPorts();
            if (_graph.GroupCount > 0) PortGeometry.LayoutGroupFrames(_graph);

            VgsLog.Info("connected " + source.EntityId + " (" + eventPort.Key + ") -> "
                        + (toGroup ? "group:" + target.GroupName : target.EntityId.ToString())
                        + " (" + action.Key + ") " + values + " @ " + cell);
            return true;
        }

        /// <summary>
        /// Rewrites the item of an existing connection with new arguments.
        ///
        /// <b>The cell is not cleared first.</b> Clearing and rewriting would leave the cell empty
        /// for the duration of two batches, and a timer firing in between would run nothing — the
        /// connection would be lost for a tick. One batch overwrites the item, and the read-back
        /// check is the one every other write uses.
        ///
        /// <b>The graph follows the toolbar, never the other way round.</b> A refused write leaves
        /// the connection with its old arguments, which is what the player sees; reporting the new
        /// ones would be a promise the block does not keep (ADR-0011, ADR-0018).
        /// </summary>
        private bool WriteEditedParameters(ParameterSet values)
        {
            Edge edge = _paramEdge;
            Node node = _paramTarget;
            Port action = _paramAction;
            if (edge == null || node == null || action == null) return false;

            // Nothing changed: no write, no refresh, and the dialog closes as if confirmed.
            if (edge.Parameters.SameAs(values)) return true;

            IMyTerminalBlock sourceBlock = ResolveBlock(edge.SourceEntityId);
            Node sourceNode = _graph.GetNode(edge.SourceEntityId);
            if (sourceBlock == null || sourceNode == null)
            {
                Warn(VgsText.WarnBlockGone);
                return false;
            }

            ParameterSet wanted = values;
            ToolbarWriter.WriteResult result = ToolbarWriter.TryRewrite(
                sourceBlock, sourceNode, edge.Cell, action.Key, wanted,
                contents => ToolbarParameters.SameItem(contents, edge.WithParameters(wanted)));

            if (result != ToolbarWriter.WriteResult.Ok)
            {
                Warn(DescribeWriteFailure(result, sourceNode, node, action));
                return false;
            }

            if (!_graph.TryReplaceEdgeParameters(edge, wanted))
            {
                // The connection left the graph while the dialog was open (the block was ground
                // down, or the player cleared the cell in the terminal). The block holds the new
                // item; the next reconcile pass turns it back into an edge.
                VgsLog.Warn("parameters written but the connection was gone: " + edge);
            }

            _graph.LayoutPorts();
            if (_graph.GroupCount > 0) PortGeometry.LayoutGroupFrames(_graph);

            VgsLog.Info("parameters of " + action.Key + " changed to " + wanted + " @ " + edge.Cell);
            return true;
        }

        /// <summary>Closes the dialog and drops every reference it held.</summary>
        private void CloseParameterDialog()
        {
            _paramForm = null;
            _paramAction = null;
            _paramTarget = null;
            _paramBindSource = null;
            _paramBindPort = null;
            _paramEdge = null;
            _paramHotButton = 0;
            _paramHotField = -1;

            // The caret and selection offsets belong to the field that had the keyboard, and the
            // next dialog's fields are different objects. Dropping the cache here is what keeps a
            // reopened dialog from drawing its caret where the previous one left off.
            _paramOffsetsValid = false;
            _paramOffsetsText = null;

            // The hint cache belongs to the dialog that just closed: the next one has its own
            // fields, and a stale `true` would reserve a hint line for a form that has none.
            _paramHintsValid = false;
            _paramHasHints = false;
        }

        /// <summary>
        /// Abandons the dialog. In create mode the bind is dropped too, because the player
        /// cancelled the request the bind was making.
        /// </summary>
        private void CancelParameters()
        {
            bool wasCreating = _paramMode == ParameterDialogMode.Create;
            CloseParameterDialog();

            if (wasCreating) CancelBind();
        }

        /// <summary>
        /// The sentence for a refused value. It names the action and what is wrong with the field,
        /// because "invalid value" alone leaves the player guessing which of three numbers was the
        /// problem.
        ///
        /// <b>A value outside the argument's own allowed list gets its own sentence.</b> "Not of
        /// the required type" would be a lie about a direction of 7: it is a number, and the type
        /// holds it fine — the action just has no meaning for it (ADR-0051).
        /// </summary>
        private string DescribeParameterError(ParameterError error, Port action)
        {
            string name = action != null ? action.DisplayName : VgsText.ParameterDialogTitle;

            if (error == ParameterError.NotAllowed)
            {
                ParameterSpec spec = _paramForm != null ? _paramForm.SpecAt(_paramForm.ErrorIndex) : null;
                return string.Format(VgsText.WarnParameterNotAllowedFormat, name,
                                     DescribeAllowedValues(spec));
            }

            return error == ParameterError.OutOfRange
                ? string.Format(VgsText.WarnParameterRangeFormat, name)
                : string.Format(VgsText.WarnParameterInvalidFormat, name);
        }

        /// <summary>The allowed values of a fixed list as one comma-separated line, for the refusal.</summary>
        private static string DescribeAllowedValues(ParameterSpec spec)
        {
            if (spec == null || !spec.IsChoice) return string.Empty;

            var parts = new System.Text.StringBuilder(24);
            for (int i = 0; i < spec.ChoiceCount; i++)
            {
                if (parts.Length > 0) parts.Append(", ");
                parts.Append(spec.ChoiceAt(i));
            }
            return parts.ToString();
        }

        /// <summary>
        /// Removes every connection of one input bar — one request, not the whole action
        /// (ADR-0050).
        ///
        /// Same order as the port's removal: the toolbar first, the graph second. A cell that
        /// refuses to clear (a remote client) keeps its connection and the player is told why,
        /// because dropping the edge alone would let the next reconcile pass rebuild the line.
        /// </summary>
        private void RemoveBarConnections(Node node, InputBar bar)
        {
            if (node == null || bar == null) return;

            _removeBuffer.Clear();
            _graph.CollectEdgesForBar(node, bar, _removeBuffer);
            if (_removeBuffer.Count == 0) return;

            long warnedAbout = 0;

            for (int i = 0; i < _removeBuffer.Count; i++)
            {
                Edge edge = _removeBuffer[i];

                IMyTerminalBlock sourceBlock = ResolveBlock(edge.SourceEntityId);
                if (sourceBlock == null)
                {
                    // The source is gone, so its toolbar is gone with it: there is nothing to
                    // clear and the edge is simply dropped.
                    _graph.RemoveEdge(edge);
                    continue;
                }

                if (ReadCellState(sourceBlock, edge) != CellState.HoldsThis)
                {
                    // The cell holds something else (edited in the terminal) or could not be
                    // read. Removing the edge alone would bring the line back on the next
                    // refresh, so the connection is kept and the refusal is reported once.
                    WarnRemoveRefused(node, ref warnedAbout);
                    continue;
                }

                Node sourceNode = _graph.GetNode(edge.SourceEntityId);
                if (!ToolbarWriter.TryClear(sourceBlock, sourceNode, edge.Cell))
                {
                    WarnRemoveRefused(node, ref warnedAbout);
                    continue;
                }

                _graph.RemoveEdge(edge);
            }

            _removeBuffer.Clear();

            // The bar disappears with its last connection, so the stacks and the frames that
            // enclose them are recomputed here rather than a refresh later.
            _graph.LayoutPorts();
            if (_graph.GroupCount > 0) PortGeometry.LayoutGroupFrames(_graph);
        }

        // ------------------------------------------------------------------ keyboard

        /// <summary>
        /// The dialog's keyboard: typing goes into the focused field, Tab and the arrows move
        /// between fields, Enter confirms and Esc cancels.
        ///
        /// <b>The character stream is drained unconditionally</b>, exactly as the search field does
        /// (ADR-0024): a character left in the game's buffer would arrive on the next frame, after
        /// the dialog had closed, and land in the search field behind it.
        ///
        /// <b>Esc is not here.</b> It arrives through <see cref="OnCancelling"/>, which asks the
        /// dialog first and then the menu, the bind and the watch — one press, one effect. This
        /// method deliberately does not look at Esc: handling it in both places would cancel the
        /// dialog and then the bind behind it on the same keystroke.
        /// </summary>
        private void HandleParameterKeyboard()
        {
            if (_paramForm == null) return;

            VRage.ModAPI.IMyInput input = MyAPIGateway.Input;
            if (input == null) return;

            TextEditor editor = _paramForm.EditorAt(_paramForm.Focus);
            bool control = input.IsAnyCtrlKeyPressed();
            bool shift = input.IsAnyShiftKeyPressed();

            if (control && editor != null)
            {
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.A)) editor.SelectAll();
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.C)) CopyParameterText(editor, false);
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.X)) CopyParameterText(editor, true);
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.V)) PasteParameterText(editor);
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Tab))
            {
                if (shift) _paramForm.FocusPrevious();
                else _paramForm.FocusNext();
                return;
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Up)) _paramForm.FocusPrevious();
            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Down)) _paramForm.FocusNext();

            if (editor != null)
            {
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.Left)) editor.MoveHorizontal(-1, shift);
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.Right)) editor.MoveHorizontal(1, shift);
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.Home)) editor.MoveTo(0, shift);
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.End))
                {
                    editor.MoveTo(editor.Text.Length, shift);
                }
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.Delete)) editor.DeleteForward();
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.Back)) editor.DeleteBackward();
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Enter))
            {
                ConfirmParameters();
                return;
            }

            // <b>Esc is not handled here.</b> The game delivers it through the screen's
            // `OnCancelling`, which is where every other "cancel what you started last" step
            // lives; consuming it in two places would cancel the dialog and then cancel the bind
            // behind it — or close the editor — on one press.
            VRage.Collections.ListReader<char> typed = input.TextInput;
            for (int i = 0; i < typed.Count; i++)
            {
                AppendParameterChar(typed[i]);
            }
        }

        /// <summary>
        /// Inserts one typed character into the focused field.
        ///
        /// <b>A number field refuses letters as they are typed.</b> The value is validated on
        /// confirm anyway, so this is not the safety net — it is the courtesy of not letting a
        /// player type a word into a field that can only hold a number. Text fields take anything
        /// printable, because a script argument really is arbitrary text.
        /// </summary>
        private void AppendParameterChar(char c)
        {
            if (_paramForm == null) return;

            TextEditor editor = _paramForm.EditorAt(_paramForm.Focus);
            if (editor == null) return;

            if (char.IsControl(c)) return;

            TypeCode code = _paramForm.TypeAt(_paramForm.Focus);
            if (code != TypeCode.String && !IsParameterCharAllowed(c)) return;

            // A field that fills several parameters holds several values, so the separator has to
            // be typeable even though it is not part of any one number (ADR-0052 §2).
            ParameterSpec spec = _paramForm.SpecAt(_paramForm.Focus);
            if (spec != null && spec.Slots > 1 && c == ',') { editor.Insert(c); return; }

            editor.Insert(c);
        }

        /// <summary>
        /// True when a character can appear in a numeric or boolean field: digits, the decimal and
        /// group separators, a sign, an exponent, and the letters of "true"/"false".
        ///
        /// The list is deliberately permissive — the real check is
        /// <see cref="ActionParameter.TryCreate"/>, which knows the type — and deliberately not
        /// empty: a field that accepted every letter would let the player type a sentence and
        /// learn only on confirm that it was never going to be a number.
        /// </summary>
        private static bool IsParameterCharAllowed(char c)
        {
            if (char.IsDigit(c)) return true;

            switch (c)
            {
                case '.':
                case ',':
                case '-':
                case '+':
                case 'e':
                case 'E':
                case 't':
                case 'r':
                case 'u':
                case 'f':
                case 'a':
                case 'l':
                case 's':
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Copies the selection, or the whole field, into the mod's own clipboard.</summary>
        private static void CopyParameterText(TextEditor editor, bool cut)
        {
            if (editor == null) return;
            if (!editor.HasSelection && cut) return;

            string text = editor.HasSelection ? editor.SelectedText : editor.Text;
            if (string.IsNullOrEmpty(text)) return;

            TextClipboard.Copy(text);

            if (cut) editor.DeleteForward();
        }

        /// <summary>Pastes the mod's own clipboard at the caret (ADR-0027: the OS one is unreachable).</summary>
        private static void PasteParameterText(TextEditor editor)
        {
            if (editor == null) return;

            string text = TextClipboard.PasteText;
            if (string.IsNullOrEmpty(text)) return;

            editor.Insert(text);
        }
    }
}






