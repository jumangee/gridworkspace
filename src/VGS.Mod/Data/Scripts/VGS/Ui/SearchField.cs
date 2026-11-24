using System;
using System.Collections.Generic;
using VRageMath;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Ui
{
    /// <summary>
    /// The block-search field as a thing of its own: the query, the caret and the selection, the
    /// horizontal scroll that keeps the caret visible, and the cached positions the draw path
    /// reads (ADR-0023, ADR-0024).
    ///
    /// <b>Why it is a type and not eight fields on the screen.</b> These values only mean
    /// something together: "where the caret is" is meaningless without "what is selected", and
    /// both are meaningless without "how far the text is scrolled". Kept as loose fields on a
    /// five-thousand-line class, every edit site had to remember to invalidate the layout and to
    /// restart the blink — and one of them eventually would not. Here the rule is in one place:
    /// <see cref="Touch"/> is the only way to change the text, and it does both.
    ///
    /// <b>What it does not own.</b> The rectangle of the field (the screen's bottom-bar layout),
    /// the colours it is painted in (the screen's palette), and the filter built from the query
    /// (<see cref="BlockSearch"/> over the graph's nodes). Those are the screen's business; this
    /// type is the field.
    ///
    /// <b>Time comes from the caller.</b> The blink phase is measured against the screen's own
    /// frame counter (ADR-0026), which is passed in: this type must not invent a clock, and a
    /// paused session would freeze any counter it could reach on its own.
    /// </summary>
    public sealed class SearchField
    {
        /// <summary>
        /// Measures a string in client-area units. Injected rather than called directly: the real
        /// implementation is <c>VgsScreenDraw.Measure</c>, which needs the game, and the arithmetic
        /// that matters here — where the caret lands, how far the text scrolls, which slice is
        /// painted — is exactly the part that should be checkable without starting the game.
        /// </summary>
        private readonly Func<string, float> _measure;

        /// <summary>
        /// The editing state: the query, the caret and the selection anchor. The rules for how
        /// those three stay consistent are in <see cref="TextEditor"/>, which is testable without
        /// the game.
        /// </summary>
        private readonly TextEditor _editor = new TextEditor(VgsConstants.SearchMaxLength);

        /// <summary>
        /// Cached caret and selection positions, in client-area units from the field's text
        /// origin, plus the horizontal scroll.
        ///
        /// Measured when the text, the caret or the field's width changes, and reused across
        /// frames: the draw path runs sixty times a second, these numbers move only when the
        /// player types, clicks or holds an arrow key.
        /// </summary>
        private bool _layoutValid;
        private float _caretX;
        private float _selectionStartX;
        private float _selectionEndX;
        private float _scrollX;

        /// <summary>Width the layout was computed for; a change (the "x" appearing) invalidates it.</summary>
        private float _areaWidth = -1f;

        /// <summary>
        /// The part of the query that is actually painted, and the index it starts at.
        ///
        /// The screen-space API has no scissor a mod may safely use here, so the left end of an
        /// overflowed query is handled by not drawing it at all: the visible tail is sliced once
        /// when the layout changes and reused every frame. Slicing in the draw path would allocate
        /// a string per frame for as long as the query was too long.
        ///
        /// The field is the full query reference when nothing is scrolled off, so the common case
        /// allocates nothing at all.
        /// </summary>
        private string _displayText;
        private float _firstVisibleX;

        /// <summary>
        /// The frame the caret's blink cycle restarts from. Set by every edit, so a keystroke
        /// never lands on the dark half of the cycle and looks like it did nothing.
        /// </summary>
        private int _blinkBaseFrame;

        /// <summary>
        /// Set by every change to the query and cleared by the caller once it has rebuilt its
        /// filter from <see cref="Text"/>.
        ///
        /// A flag rather than an event: an event would be a delegate invocation per keystroke and
        /// a subscriber that must not be forgotten, and there is exactly one reader — the screen
        /// that owns this field.
        /// </summary>
        private bool _changed;

        /// <summary>
        /// Creates a field that measures text through <paramref name="measure"/>.
        ///
        /// The measurer returns the width of a string in the same units as the field's rectangle,
        /// and must be a pure function of the string: the cached layout assumes that the same text
        /// always measures the same.
        /// </summary>
        public SearchField(Func<string, float> measure)
        {
            if (measure == null) throw new ArgumentNullException("measure");
            _measure = measure;
        }

        /// <summary>
        /// True while the pointer is over the field. Written by the input pass, read by the draw
        /// pass; a property rather than a field so the write is a named, findable member.
        /// </summary>
        public bool Hot { get; set; }

        /// <summary>True while the pointer is over the field's clear button.</summary>
        public bool ClearHot { get; set; }

        /// <summary>The query as typed. Never null.</summary>
        public string Text
        {
            get { return _editor.Text; }
        }

        /// <summary>True while the field holds any text, which is when the "x" is drawn.</summary>
        public bool HasText
        {
            get { return _editor.Length > 0; }
        }

        /// <summary>The caret index in the query, 0..Length.</summary>
        public int Caret
        {
            get { return _editor.Caret; }
        }

        /// <summary>True while a part of the query is selected.</summary>
        public bool HasSelection
        {
            get { return _editor.HasSelection; }
        }

        /// <summary>Index of the first selected character.</summary>
        public int SelectionStart
        {
            get { return _editor.SelectionStart; }
        }

        /// <summary>Index one past the last selected character.</summary>
        public int SelectionEnd
        {
            get { return _editor.SelectionEnd; }
        }

        /// <summary>The selected part of the query; empty when nothing is selected.</summary>
        public string SelectedText
        {
            get { return _editor.SelectedText; }
        }

        /// <summary>X of the caret, in client-area units from the text origin.</summary>
        public float CaretX
        {
            get { return _caretX; }
        }

        /// <summary>X of the selection's left edge, in the same units.</summary>
        public float SelectionStartX
        {
            get { return _selectionStartX; }
        }

        /// <summary>X of the selection's right edge, in the same units.</summary>
        public float SelectionEndX
        {
            get { return _selectionEndX; }
        }

        /// <summary>How far the text is scrolled left, in the same units.</summary>
        public float ScrollX
        {
            get { return _scrollX; }
        }

        /// <summary>The slice of the query that is painted; null before the first layout.</summary>
        public string DisplayText
        {
            get { return _displayText; }
        }

        /// <summary>X of the first painted character, in the same units.</summary>
        public float FirstVisibleX
        {
            get { return _firstVisibleX; }
        }

        /// <summary>
        /// True once since the last <see cref="TakeChanged"/>, meaning the query changed and the
        /// caller's filter is stale.
        /// </summary>
        public bool TakeChanged()
        {
            if (!_changed) return false;
            _changed = false;
            return true;
        }

        /// <summary>
        /// Marks the query as changed and invalidates the layout. The single place that does
        /// both, so a new edit path cannot update one without the other.
        /// </summary>
        private void Touch(int frame)
        {
            _changed = true;
            _layoutValid = false;
            _blinkBaseFrame = frame;
        }

        /// <summary>
        /// Inserts one typed character at the caret. Control characters are dropped: the game
        /// hands over the raw character stream, which includes the carriage return of Enter, the
        /// backspace and whatever a Ctrl shortcut produces (ADR-0024).
        ///
        /// Returns true when the text actually changed.
        /// </summary>
        public bool Insert(char c, int frame)
        {
            if (char.IsControl(c)) return false;
            if (!_editor.Insert(c)) return false;

            Touch(frame);
            return true;
        }

        /// <summary>Inserts a whole string at the caret (a paste). False when it did not fit.</summary>
        public bool Insert(string text, int frame)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (!_editor.Insert(text)) return false;

            Touch(frame);
            return true;
        }

        /// <summary>Deletes the character before the caret. False when there was none.</summary>
        public bool Backspace(int frame)
        {
            if (!_editor.DeleteBackward()) return false;

            Touch(frame);
            return true;
        }

        /// <summary>Deletes the character at the caret. False when there was none.</summary>
        public bool DeleteForward(int frame)
        {
            if (!_editor.DeleteForward()) return false;

            Touch(frame);
            return true;
        }

        /// <summary>
        /// Moves the caret by <paramref name="delta"/> characters; <paramref name="extend"/>
        /// keeps the selection anchor where it is (Shift).
        ///
        /// The blink restarts even without a text change: the player just moved the insertion
        /// point and must be able to see it.
        /// </summary>
        public void MoveHorizontal(int delta, bool extend, int frame)
        {
            _editor.MoveHorizontal(delta, extend);

            _layoutValid = false;
            _blinkBaseFrame = frame;
        }

        /// <summary>Moves the caret to an absolute index; <paramref name="extend"/> as above.</summary>
        public void MoveTo(int index, bool extend, int frame)
        {
            _editor.MoveTo(index, extend);

            _layoutValid = false;
            _blinkBaseFrame = frame;
        }

        /// <summary>Selects the whole query.</summary>
        public bool SelectAll(int frame)
        {
            if (_editor.Length == 0) return false;

            _editor.SelectAll();
            _layoutValid = false;
            _blinkBaseFrame = frame;
            return true;
        }

        /// <summary>Drops the query entirely, which brings every card back to full opacity.</summary>
        public void Clear(int frame)
        {
            _editor.Clear();
            Touch(frame);
        }

        /// <summary>
        /// Moves the caret to the character boundary nearest <paramref name="targetX"/> (a click
        /// in the field). <paramref name="extend"/> keeps the anchor, for a Shift-click.
        /// </summary>
        public void PlaceCaretAt(float targetX, bool extend, int frame)
        {
            string text = _editor.Text;
            int nearest = 0;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i <= text.Length; i++)
            {
                float distance = System.Math.Abs(PrefixWidth(i) - targetX);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = i;
            }

            _editor.MoveTo(nearest, extend);
            _layoutValid = false;
            _blinkBaseFrame = frame;
        }

        /// <summary>
        /// Recomputes the caret and selection positions, the scroll, and the slice of the query
        /// that is painted.
        ///
        /// Runs when the text, the caret or the field's width changed — never per frame — because
        /// it calls the font metrics once per character boundary. This is the whole reason the
        /// positions are cached.
        /// </summary>
        public void EnsureLayout(float areaWidth)
        {
            if (_layoutValid && _areaWidth == areaWidth) return;

            _areaWidth = areaWidth;

            string text = _editor.Text;
            int caret = _editor.Caret;

            _caretX = PrefixWidth(caret);

            if (_editor.HasSelection)
            {
                _selectionStartX = PrefixWidth(_editor.SelectionStart);
                _selectionEndX = PrefixWidth(_editor.SelectionEnd);
            }
            else
            {
                _selectionStartX = _caretX;
                _selectionEndX = _caretX;
            }

            // ---- keep the caret inside the visible span ----
            float total = PrefixWidth(text.Length);
            float scroll = _scrollX;

            if (_caretX - scroll > areaWidth) scroll = _caretX - areaWidth;
            if (_caretX - scroll < 0f) scroll = _caretX;
            if (scroll > total - areaWidth) scroll = total - areaWidth;
            if (scroll < 0f) scroll = 0f;

            _scrollX = scroll;

            // ---- the slice that is painted ----
            //
            // Sliced here rather than in the draw path: the visible tail is the same string on
            // every frame until the text scrolls again, and a Substring per frame would be an
            // allocation per frame for a field nobody is typing into.
            if (scroll <= 0f)
            {
                _displayText = text;
                _firstVisibleX = 0f;
            }
            else
            {
                int first = 0;
                while (first < text.Length && PrefixWidth(first) < scroll) first++;

                _firstVisibleX = PrefixWidth(first);
                _displayText = first == 0 ? text : text.Substring(first);
            }

            _layoutValid = true;
        }

        /// <summary>
        /// True on the lit half of the blink cycle at <paramref name="frame"/>.
        ///
        /// A caret that is always on is hard to find against a glyph; one that is always off is
        /// not a caret. The half-period is <see cref="VgsConstants.CaretBlinkFrames"/>, and the
        /// frame counter belongs to the caller — no timer, so this costs one subtraction.
        /// </summary>
        public bool CaretVisible(int frame)
        {
            int elapsed = frame - _blinkBaseFrame;
            if (elapsed < 0) elapsed = 0;
            return elapsed % (VgsConstants.CaretBlinkFrames * 2) < VgsConstants.CaretBlinkFrames;
        }

        /// <summary>
        /// Width of the first <paramref name="count"/> characters of the query, in client-area
        /// units.
        ///
        /// A guarded call into the font metrics: it is the only way to know where a character
        /// boundary lies, and it is used only when the layout is rebuilt. The whole string needs
        /// no slice, which is the case a caret at the end of the query hits on every keystroke.
        /// </summary>
        private float PrefixWidth(int count)
        {
            if (count <= 0) return 0f;

            string text = _editor.Text;
            if (count >= text.Length) return _measure(text);

            return _measure(text.Substring(0, count));
        }
    }
}
