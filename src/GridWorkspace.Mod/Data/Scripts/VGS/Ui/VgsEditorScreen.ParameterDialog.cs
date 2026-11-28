using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRageMath;
using Vgs.Core;
using Vgs.Model;
using static Vgs.Ui.VgsPalette;

namespace Vgs.Ui
{
    /// <summary>
    /// The argument dialog's geometry, its drawing and its pointer handling (ADR-0050).
    ///
    /// A partial of <see cref="VgsEditorScreen"/>. The behaviour — opening, validating, writing,
    /// cancelling, the keyboard — is in <c>VgsEditorScreen.Parameters.cs</c>. The two are split
    /// because a dialog is exactly the kind of thing where the rectangle that is painted and the
    /// rectangle that is clicked drift apart: here every rectangle comes from one method, and both
    /// the draw pass and the hit tests call it.
    ///
    /// <b>The dialog reads no world.</b> Everything it shows is the form's own state, and every
    /// string comes from <see cref="VgsText"/> (ADR-0003). The caret blinks against the screen's
    /// own frame counter (ADR-0026), which keeps running while the dialog is open — the session is
    /// never paused (ADR-0029), so a value the player is typing costs the world nothing.
    /// </summary>
    public sealed partial class VgsEditorScreen
    {
        /// <summary>Gap between the dialog's edge and its contents, in client units.</summary>
        private const float ParamPadding = 0.012f;

        /// <summary>Height of one field row, in client units.</summary>
        private const float ParamRowHeight = 0.032f;

        /// <summary>
        /// Height of the line under a field that lists the allowed values, in client units.
        ///
        /// <b>Reserved for the whole dialog, not per row.</b> A direction field is the only one of
        /// the rotor's three that has such a line, and letting one row be taller would make the
        /// grid ragged — three values that belong to one request have to read as one block.
        /// </summary>
        private const float ParamHintHeight = 0.016f;

        /// <summary>Height of the line that carries a refusal, in client units.</summary>
        private const float ParamErrorHeight = 0.022f;

        /// <summary>Height of the button strip at the bottom, in client units.</summary>
        private const float ParamButtonHeight = 0.030f;

        /// <summary>Width of one button, in client units.</summary>
        private const float ParamButtonWidth = 0.080f;

        // ------------------------------------------------------------------ geometry

        /// <summary>
        /// True when any field has a line under it, which is what decides whether the dialog
        /// reserves <see cref="ParamHintHeight"/> in every row.
        /// </summary>
        private bool AnyParameterHint()
        {
            // Cached, because the geometry is asked for every field on every frame: without the
            // cache this is a loop over all fields, called three times per field, sixty times a
            // second. The hints are set once, when the dialog opens.
            if (!_paramHintsValid)
            {
                _paramHasHints = false;
                if (_paramForm != null)
                {
                    for (int i = 0; i < _paramForm.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(_paramForm.HintAt(i))) { _paramHasHints = true; break; }
                    }
                }
                _paramHintsValid = true;
            }
            return _paramHasHints;
        }

        /// <summary>The vertical pitch of one field row: the field plus its reserved hint line.</summary>
        private float ParameterRowPitch()
        {
            return AnyParameterHint() ? ParamRowHeight + ParamHintHeight : ParamRowHeight;
        }

        /// <summary>
        /// The dialog's rectangle: centred on the canvas, tall enough for its fields.
        ///
        /// <b>Centred, not anchored to the pointer.</b> The player is about to type, so where the
        /// window appears must not depend on where the last click landed — a window that jumps
        /// under the hand reads as a glitch. The action menu is anchored for the opposite reason:
        /// it is a choice about the thing under the pointer.
        /// </summary>
        private void GetParameterRect(out Vector2 topLeft, out Vector2 size)
        {
            Vector2 canvasTopLeft, canvasSize;
            CanvasRect.Get(out canvasTopLeft, out canvasSize);

            int count = _paramForm != null ? _paramForm.Count : 1;

            size = new Vector2(
                VgsConstants.ParameterDialogWidth,
                ParamPadding * 2f
                + VgsConstants.ParameterTitleHeight
                + count * ParameterRowPitch()
                + ParamErrorHeight
                + ParamButtonHeight);

            topLeft = new Vector2(
                canvasTopLeft.X + (canvasSize.X - size.X) * 0.5f,
                canvasTopLeft.Y + (canvasSize.Y - size.Y) * 0.5f);
        }

        /// <summary>Rectangle of one field's label, in client units.</summary>
        private void GetParameterLabelRect(int index, out Vector2 topLeft, out Vector2 size)
        {
            Vector2 dialogTopLeft, dialogSize;
            GetParameterRect(out dialogTopLeft, out dialogSize);

            float y = dialogTopLeft.Y + ParamPadding + VgsConstants.ParameterTitleHeight
                      + index * ParameterRowPitch();

            size = new Vector2(VgsConstants.ParameterLabelWidth, ParamRowHeight);
            topLeft = new Vector2(dialogTopLeft.X + ParamPadding, y);
        }

        /// <summary>Rectangle of one field's text box, in client units.</summary>
        private void GetParameterFieldRect(int index, out Vector2 topLeft, out Vector2 size)
        {
            Vector2 dialogTopLeft, dialogSize;
            GetParameterRect(out dialogTopLeft, out dialogSize);

            float y = dialogTopLeft.Y + ParamPadding + VgsConstants.ParameterTitleHeight
                      + index * ParameterRowPitch();

            float x = dialogTopLeft.X + ParamPadding + VgsConstants.ParameterLabelWidth;
            float width = dialogSize.X - ParamPadding * 2f - VgsConstants.ParameterLabelWidth;

            size = new Vector2(width, ParamRowHeight - 0.006f);
            topLeft = new Vector2(x, y + 0.003f);
        }

        /// <summary>
        /// Rectangle of the line under one field, in client units: the field's own width, one
        /// <see cref="ParamHintHeight"/> tall, directly below it. Zero-sized when the dialog has no
        /// hints at all, so a caller that draws unconditionally paints nothing.
        /// </summary>
        private void GetParameterHintRect(int index, out Vector2 topLeft, out Vector2 size)
        {
            Vector2 fieldTopLeft, fieldSize;
            GetParameterFieldRect(index, out fieldTopLeft, out fieldSize);

            if (!AnyParameterHint())
            {
                topLeft = new Vector2(fieldTopLeft.X, fieldTopLeft.Y + fieldSize.Y);
                size = Vector2.Zero;
                return;
            }

            topLeft = new Vector2(fieldTopLeft.X, fieldTopLeft.Y + ParamRowHeight - 0.006f);
            size = new Vector2(fieldSize.X, ParamHintHeight);
        }

        /// <summary>
        /// Rectangle of a button: 0 is confirm, 1 is cancel. Right-aligned, cancel last — the
        /// order every dialog in the game uses, and the one the player's hand expects.
        /// </summary>
        private void GetParameterButtonRect(int index, out Vector2 topLeft, out Vector2 size)
        {
            Vector2 dialogTopLeft, dialogSize;
            GetParameterRect(out dialogTopLeft, out dialogSize);

            size = new Vector2(ParamButtonWidth, ParamButtonHeight);

            float y = dialogTopLeft.Y + dialogSize.Y - ParamPadding - ParamButtonHeight;
            float right = dialogTopLeft.X + dialogSize.X - ParamPadding;

            float x = index == 1
                ? right - ParamButtonWidth
                : right - ParamButtonWidth * 2f - 0.006f;

            topLeft = new Vector2(x, y);
        }

        /// <summary>
        /// Which field a point is over, or -1. Used for the highlight and for the click, so the
        /// two cannot disagree about where a field is.
        /// </summary>
        private int ParameterFieldAt(Vector2 mouse)
        {
            if (_paramForm == null) return -1;

            for (int i = 0; i < _paramForm.Count; i++)
            {
                Vector2 topLeft, size;
                GetParameterFieldRect(i, out topLeft, out size);
                if (PortGeometry.Contains(topLeft, size, mouse)) return i;
            }
            return -1;
        }

        /// <summary>Which button a point is over: 0 none, 1 confirm, 2 cancel.</summary>
        private int ParameterButtonAt(Vector2 mouse)
        {
            for (int i = 0; i < 2; i++)
            {
                Vector2 topLeft, size;
                GetParameterButtonRect(i, out topLeft, out size);
                if (PortGeometry.Contains(topLeft, size, mouse)) return i + 1;
            }
            return 0;
        }

        // ------------------------------------------------------------------ pointer

        /// <summary>
        /// The dialog's pointer handling, called instead of every other mode while it is open.
        ///
        /// <b>It consumes the pointer completely.</b> The dialog is modal: a click outside it must
        /// not pan the canvas, select a card or start a gesture behind a window the player is
        /// typing into. That is the same rule the action menu follows, and the reason both are
        /// branches in the input handler rather than overlays painted on top.
        /// </summary>
        private void HandleParameterDialogInput(Vector2 mouse)
        {
            _paramHotField = ParameterFieldAt(mouse);
            _paramHotButton = ParameterButtonAt(mouse);

            if (!MyAPIGateway.Input.IsNewLeftMousePressed()) return;

            if (_paramHotField >= 0)
            {
                _paramForm.FocusAt(_paramHotField);

                // The caret goes where the click landed, measured from the field's own text
                // origin — the same "a click places the caret" rule the search field follows.
                Vector2 fieldTopLeft, fieldSize;
                GetParameterFieldRect(_paramHotField, out fieldTopLeft, out fieldSize);

                TextEditor editor = _paramForm.EditorAt(_paramHotField);
                if (editor != null)
                {
                    editor.MoveTo(NearestCharacter(editor.Text,
                                                   mouse.X - fieldTopLeft.X - VgsConstants.PortLabelPadding,
                                                   ParamTextScale),
                                  false);
                }
                return;
            }

            if (_paramHotButton == 1) ConfirmParameters();
            else if (_paramHotButton == 2) CancelParameters();
        }

        /// <summary>
        /// The character boundary nearest a horizontal offset inside a field. Used only on a
        /// click, so it may measure per boundary: the field holds one short value.
        /// </summary>
        private static int NearestCharacter(string text, float targetX, float scale)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            if (targetX <= 0f) return 0;

            int nearest = 0;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i <= text.Length; i++)
            {
                float width = i == 0 ? 0f : VgsScreenDraw.Measure(text.Substring(0, i), scale).X;
                float distance = Math.Abs(width - targetX);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = i;
            }
            return nearest;
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>Font scale of the dialog's own text: fields, labels and the refusal line.</summary>
        private const float ParamTextScale = VgsConstants.NodeSubtitleScale * 1.05f;

        /// <summary>
        /// Cached height of one line of the dialog's text, in client units, or a negative value
        /// before it has been measured.
        ///
        /// <b>Measured once, not per field per frame.</b> The height comes from the font and the
        /// scale, neither of which changes while the screen lives, and the dialog draws one line
        /// per field on every frame. <see cref="PortLabelHeight"/> caches its own line for exactly
        /// this reason.
        /// </summary>
        private float _paramLineHeight = -1f;

        /// <summary>
        /// Whether any field has a hint line, cached: the row pitch depends on it and the pitch is
        /// read per field per frame. Invalidated when the dialog opens or closes.
        /// </summary>
        private bool _paramHintsValid;
        private bool _paramHasHints;

        /// <summary>
        /// Drops the cached hint flag. Called when a dialog opens (its fields were just labelled)
        /// and when one closes.
        /// </summary>
        private void InvalidateParameterHints()
        {
            _paramHintsValid = false;
            _paramHasHints = false;
        }

        /// <summary>
        /// Cached caret and selection offsets of the focused field, in client units from the
        /// field's text origin.
        ///
        /// <b>Why a cache and not a measurement.</b> The offsets are needed every frame to draw
        /// the caret and the selection, and measuring them means measuring a <em>prefix</em> of
        /// the text — which slices a string. Doing that sixty times a second for a field nobody is
        /// typing in would allocate for nothing; the numbers only change when the text, the caret
        /// or the selection does. The invalidation key is therefore the whole state they depend
        /// on, compared by value.
        /// </summary>
        private bool _paramOffsetsValid;
        private int _paramOffsetsField = -1;
        private string _paramOffsetsText;
        private int _paramOffsetsCaret = -1;
        private int _paramOffsetsAnchor = -1;
        private float _paramCaretX;
        private float _paramSelectionStartX;
        private float _paramSelectionEndX;

        /// <summary>
        /// Recomputes the cached offsets if the focused field's state changed. Called from the draw
        /// path, which may therefore call it every frame without cost.
        /// </summary>
        private void RefreshParameterOffsets(TextEditor editor)
        {
            if (editor == null) return;

            if (_paramOffsetsValid
                && _paramOffsetsField == _paramForm.Focus
                && _paramOffsetsCaret == editor.Caret
                && _paramOffsetsAnchor == editor.Anchor
                && ReferenceEquals(_paramOffsetsText, editor.Text))
            {
                return;
            }

            _paramOffsetsValid = true;
            _paramOffsetsField = _paramForm.Focus;
            _paramOffsetsCaret = editor.Caret;
            _paramOffsetsAnchor = editor.Anchor;
            _paramOffsetsText = editor.Text;

            _paramCaretX = MeasurePrefix(editor.Text, editor.Caret);

            if (editor.HasSelection)
            {
                _paramSelectionStartX = MeasurePrefix(editor.Text, editor.SelectionStart);
                _paramSelectionEndX = MeasurePrefix(editor.Text, editor.SelectionEnd);
            }
            else
            {
                _paramSelectionStartX = 0f;
                _paramSelectionEndX = 0f;
            }
        }

        /// <summary>
        /// Draws the dialog: a panel, the action's caption, one labelled field per argument, the
        /// refusal line and two buttons.
        ///
        /// <b>The refusal line is always reserved, never inserted.</b> A dialog that grew and
        /// shrank as the player typed would move its own buttons under the pointer, and the
        /// button that moved is the one that commits.
        /// </summary>
        private void DrawParameterDialog()
        {
            if (_paramForm == null) return;

            Vector2 topLeft, size;
            GetParameterRect(out topLeft, out size);

            // The backdrop under the dialog: the picture behind it stays visible but is clearly
            // not interactive, the same way the action menu reads (ADR-0024 §5).
            VgsScreenDraw.Panel(topLeft, size, ColMenu, ColMenuBorder, Hairline);

            string title = _paramAction != null ? _paramAction.DisplayName : VgsText.ParameterDialogTitle;
            VgsScreenDraw.Text(title,
                               topLeft + new Vector2(ParamPadding, ParamPadding * 0.5f),
                               VgsConstants.NodeTitleScale * 0.85f, ColText,
                               size.X - ParamPadding * 2f);

            for (int i = 0; i < _paramForm.Count; i++)
            {
                DrawParameterField(i);
            }

            if (_paramForm.Error != ParameterError.None)
            {
                float y = topLeft.Y + size.Y - ParamPadding - ParamButtonHeight - ParamErrorHeight;

                VgsScreenDraw.Text(DescribeParameterError(_paramForm.Error, _paramAction),
                                   new Vector2(topLeft.X + ParamPadding, y),
                                   VgsConstants.NodeSubtitleScale * 0.9f, ColWarn,
                                   size.X - ParamPadding * 2f);
            }

            DrawParameterButton(0, VgsText.ParameterConfirm);
            DrawParameterButton(1, VgsText.ParameterCancel);
        }

        /// <summary>
        /// Draws one field: its label, its box, its text, and — when it has the keyboard — its
        /// selection and its caret.
        ///
        /// <b>The field with the keyboard is outlined, not filled differently.</b> The fill says
        /// "this is an input"; the frame says "this one takes the keys". A wrong value turns that
        /// frame red, which is why the error line can be one sentence for the whole form.
        ///
        /// <b>The text is clipped to the box.</b> A script argument can be longer than the field is
        /// wide, and a field that spilled its text over the dialog's edge would hide the buttons.
        /// </summary>
        private void DrawParameterField(int index)
        {
            Vector2 labelTopLeft, labelSize;
            GetParameterLabelRect(index, out labelTopLeft, out labelSize);

            if (_paramLineHeight < 0f) _paramLineHeight = VgsScreenDraw.Measure("Ag", ParamTextScale).Y;
            float lineHeight = _paramLineHeight;

            VgsScreenDraw.Text(_paramForm.LabelAt(index),
                               new Vector2(labelTopLeft.X,
                                           PortGeometry.CentredTextTop(labelTopLeft.Y, labelSize.Y,
                                                                       lineHeight)),
                               ParamTextScale, ColTextDim, labelSize.X);

            Vector2 fieldTopLeft, fieldSize;
            GetParameterFieldRect(index, out fieldTopLeft, out fieldSize);

            bool focused = index == _paramForm.Focus;
            bool hot = index == _paramHotField;
            bool bad = _paramForm.ErrorIndex == index && _paramForm.Error != ParameterError.None;

            Color frame = bad ? ColWarn : (focused || hot ? ColMenuBorder : ColBarSeparator);

            VgsScreenDraw.Rect(fieldTopLeft, fieldSize, ColField);
            VgsScreenDraw.Border(fieldTopLeft, fieldSize, frame, Hairline);

            TextEditor editor = _paramForm.EditorAt(index);
            if (editor == null) return;

            float textX = fieldTopLeft.X + VgsConstants.PortLabelPadding;
            float textWidth = fieldSize.X - VgsConstants.PortLabelPadding * 2f;
            float textY = PortGeometry.CentredTextTop(fieldTopLeft.Y, fieldSize.Y, lineHeight);

            if (focused)
            {
                RefreshParameterOffsets(editor);

                // Selection first, under the text: the highlight must not paint over the glyphs.
                if (editor.HasSelection)
                {
                    float left = _paramSelectionStartX < _paramSelectionEndX
                        ? _paramSelectionStartX
                        : _paramSelectionEndX;
                    float width = Math.Abs(_paramSelectionEndX - _paramSelectionStartX);

                    VgsScreenDraw.Rect(new Vector2(textX + left, fieldTopLeft.Y + 0.002f),
                                       new Vector2(width, fieldSize.Y - 0.004f), ColMenuRowHot);
                }
            }

            VgsScreenDraw.Text(editor.Text, new Vector2(textX, textY), ParamTextScale, ColText, textWidth);

            // The hint line: what this field accepts, in the player's language. It sits under the
            // field rather than in a tooltip because a tooltip needs a hover, and the values a
            // rotor direction accepts are exactly what the player must know *before* typing.
            Vector2 hintTopLeft, hintSize;
            GetParameterHintRect(index, out hintTopLeft, out hintSize);
            if (hintSize.Y > 0f)
            {
                string hint = _paramForm.HintAt(index);
                if (!string.IsNullOrEmpty(hint))
                {
                    VgsScreenDraw.Text(hint, hintTopLeft, ParamTextScale * 0.86f, ColTextDim, hintSize.X);
                }
            }

            // The caret: a one-pixel vertical line at the insertion point, on the lit half of the
            // blink cycle. A field that does not take keys shows no caret — a caret in a field
            // that ignores typing is a lie about where the next character goes.
            if (!focused) return;
            if ((_screenFrame / VgsConstants.CaretBlinkFrames) % 2 != 0) return;

            VgsScreenDraw.Rect(new Vector2(textX + _paramCaretX, fieldTopLeft.Y + 0.002f),
                               new Vector2(Hairline, fieldSize.Y - 0.004f), ColText);
        }

        /// <summary>Width of the first <paramref name="count"/> characters, in client units.</summary>
        private static float MeasurePrefix(string text, int count)
        {
            if (string.IsNullOrEmpty(text) || count <= 0) return 0f;
            if (count >= text.Length) return VgsScreenDraw.Measure(text, ParamTextScale).X;
            return VgsScreenDraw.Measure(text.Substring(0, count), ParamTextScale).X;
        }

        /// <summary>
        /// Draws one dialog button. The confirm button is the accent-filled one: the dialog has a
        /// right answer and a way out, and the two must not look alike.
        /// </summary>
        private void DrawParameterButton(int index, string caption)
        {
            Vector2 topLeft, size;
            GetParameterButtonRect(index, out topLeft, out size);

            bool hot = _paramHotButton == index + 1;
            bool primary = index == 0;

            Color fill = primary ? ColButtonPrimary : ColMenuRowHot;
            if (hot) fill = primary ? ColButtonPrimaryHot : ColMenuBorder;

            VgsScreenDraw.Rect(topLeft, size, fill);
            VgsScreenDraw.Border(topLeft, size, ColMenuBorder, Hairline);

            Vector2 measured = VgsScreenDraw.Measure(caption, ParamTextScale);

            VgsScreenDraw.Text(caption,
                               new Vector2(topLeft.X + (size.X - measured.X) * 0.5f,
                                           PortGeometry.CentredTextTop(topLeft.Y, size.Y, measured.Y)),
                               ParamTextScale, primary ? ColButtonPrimaryText : ColText, size.X);
        }
    }
}


