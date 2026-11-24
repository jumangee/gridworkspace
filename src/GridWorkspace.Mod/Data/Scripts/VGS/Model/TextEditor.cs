using System;

namespace Vgs.Model
{
    /// <summary>
    /// The editing state of a single-line text field: the text, the caret position and the
    /// selection anchor (ADR-0024).
    ///
    /// <b>Why the editor has its own text field instead of the game's.</b> The game ships
    /// <c>Sandbox.Graphics.GUI.MyGuiControlTextbox</c>, but a mod cannot use it: the IL whitelist
    /// admits 57 namespaces exactly and <c>Sandbox.Graphics</c> is not one of them, so a reference
    /// to that type compiles locally and is rejected at load with "is prohibited". Caret, selection
    /// and the editing keys therefore have to be ours. Everything that decides <em>what the text
    /// becomes</em> lives here rather than in the screen, because it is the part that can be
    /// tested without the game (<c>tools/offline-check.ps1</c>) — and the part where an off-by-one
    /// silently corrupts what the player typed.
    ///
    /// <b>Caret and anchor, not "start and length".</b> An anchor is the fixed end of a selection
    /// and the caret is the moving one; a selection is "caret != anchor". Modelling it that way
    /// makes Shift+Left and Shift+Right fall out of one rule, and it removes the class of bug where
    /// a selection length goes negative because the player extended it the other way. With no
    /// selection the two are equal, which is a single comparison instead of a second boolean that
    /// can disagree with them.
    ///
    /// <b>Every operation keeps the caret inside the text.</b> A caller never has to clamp: the
    /// screen passes raw key deltas and index hints, and this type decides what is reachable.
    /// </summary>
    public sealed class TextEditor
    {
        private readonly int _maxLength;

        private string _text;
        private int _caret;
        private int _anchor;

        /// <summary>
        /// Creates an editor that never holds more than <paramref name="maxLength"/> characters.
        ///
        /// A negative limit is treated as zero rather than as "unlimited": the value comes from a
        /// constant, and a field that silently accepted unlimited text would be a layout hazard
        /// (the text is what decides whether the caret is scrolled into view).
        /// </summary>
        public TextEditor(int maxLength)
        {
            _maxLength = maxLength < 0 ? 0 : maxLength;
            _text = string.Empty;
        }

        /// <summary>The text as it stands. Never null.</summary>
        public string Text { get { return _text; } }

        public int Length { get { return _text.Length; } }

        /// <summary>Index of the insertion point, 0..<see cref="Length"/>.</summary>
        public int Caret { get { return _caret; } }

        /// <summary>The fixed end of the selection, 0..<see cref="Length"/>.</summary>
        public int Anchor { get { return _anchor; } }

        /// <summary>True while a selection is being held, i.e. the caret and the anchor differ.</summary>
        public bool HasSelection { get { return _caret != _anchor; } }

        /// <summary>Lower end of the selection, regardless of which way it was made.</summary>
        public int SelectionStart { get { return _caret < _anchor ? _caret : _anchor; } }

        /// <summary>Upper end of the selection, regardless of which way it was made.</summary>
        public int SelectionEnd { get { return _caret < _anchor ? _anchor : _caret; } }

        /// <summary>The selected text, or an empty string when nothing is selected.</summary>
        public string SelectedText
        {
            get
            {
                if (!HasSelection) return string.Empty;
                return _text.Substring(SelectionStart, SelectionEnd - SelectionStart);
            }
        }

        /// <summary>
        /// Replaces the whole text and puts the caret at the end of it, with no selection.
        ///
        /// "At the end" rather than "wherever it was": every caller of this method is either
        /// seeding the field (a fresh query starts ready to continue typing) or resetting it.
        /// </summary>
        public void SetText(string text)
        {
            _text = text ?? string.Empty;
            if (_text.Length > _maxLength) _text = _text.Substring(0, _maxLength);
            _caret = _text.Length;
            _anchor = _caret;
        }

        /// <summary>Empties the field and parks the caret at the start.</summary>
        public void Clear()
        {
            SetText(string.Empty);
        }

        /// <summary>
        /// Inserts one character at the caret, replacing the selection when there is one.
        ///
        /// Returns false when the text is already at the limit and nothing was removed to make
        /// room — the caller uses it to decide whether anything changed at all.
        /// </summary>
        public bool Insert(char c)
        {
            return Insert(c.ToString());
        }

        /// <summary>
        /// Inserts a string at the caret, replacing the selection. A paste is this method.
        ///
        /// The inserted text is cut to the room that is left rather than rejected whole: a paste
        /// that fits partially is more useful than one that does nothing, and the alternative
        /// (refusing) silently loses the user's copy.
        ///
        /// With a selection the room is always at least one character — the text never exceeds the
        /// limit, so removing at least one character frees at least one — which is why the
        /// "no room" case below can only be reached without a selection.
        /// </summary>
        public bool Insert(string textToInsert)
        {
            if (string.IsNullOrEmpty(textToInsert)) return false;

            int start = SelectionStart;
            string withoutSelection = HasSelection
                ? _text.Remove(start, SelectionEnd - start)
                : _text;

            int room = _maxLength - withoutSelection.Length;
            if (room <= 0) return false;

            string insert = textToInsert.Length > room ? textToInsert.Substring(0, room) : textToInsert;

            _text = withoutSelection.Insert(start, insert);
            _caret = start + insert.Length;
            _anchor = _caret;
            return true;
        }

        /// <summary>
        /// Deletes the selection, or the character before the caret when there is none.
        /// Returns false when there was nothing to delete.
        /// </summary>
        public bool DeleteBackward()
        {
            if (HasSelection) return DeleteSelection();

            if (_caret <= 0) return false;

            _text = _text.Remove(_caret - 1, 1);
            _caret--;
            _anchor = _caret;
            return true;
        }

        /// <summary>
        /// Deletes the selection, or the character after the caret when there is none.
        /// Returns false when there was nothing to delete.
        /// </summary>
        public bool DeleteForward()
        {
            if (HasSelection) return DeleteSelection();

            if (_caret >= _text.Length) return false;

            _text = _text.Remove(_caret, 1);
            // The caret does not move: the next character slides into its place.
            _anchor = _caret;
            return true;
        }

        /// <summary>
        /// Moves the caret one character left (-1) or right (+1).
        ///
        /// With <paramref name="extend"/> the anchor stays put and the selection grows or shrinks;
        /// without it a held selection collapses to the edge the caret is moving towards, which is
        /// what every text field does and what stops "press Right after selecting all" from
        /// leaving the whole text selected.
        /// </summary>
        public void MoveHorizontal(int direction, bool extend)
        {
            if (direction == 0) return;

            if (!extend && HasSelection)
            {
                _caret = direction < 0 ? SelectionStart : SelectionEnd;
                _anchor = _caret;
                return;
            }

            MoveTo(_caret + (direction < 0 ? -1 : 1), extend);
        }

        /// <summary>Puts the caret at <paramref name="index"/>, clamped into the text.</summary>
        public void MoveTo(int index, bool extend)
        {
            _caret = Clamp(index);
            if (!extend) _anchor = _caret;
        }

        /// <summary>Selects the whole text: the caret goes to the end, the anchor to the start.</summary>
        public void SelectAll()
        {
            _anchor = 0;
            _caret = _text.Length;
        }

        private bool DeleteSelection()
        {
            if (!HasSelection) return false;

            int start = SelectionStart;
            _text = _text.Remove(start, SelectionEnd - start);
            _caret = start;
            _anchor = start;
            return true;
        }

        private int Clamp(int index)
        {
            if (index < 0) return 0;
            if (index > _text.Length) return _text.Length;
            return index;
        }
    }
}
