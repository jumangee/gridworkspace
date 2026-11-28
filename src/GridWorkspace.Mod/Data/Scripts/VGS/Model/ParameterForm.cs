using System;
using System.Collections.Generic;
using Vgs.Core;

namespace Vgs.Model
{
    /// <summary>
    /// The argument form of one action: one field per argument, the focus, and the rule that
    /// decides whether the whole form is a valid request (ADR-0050).
    ///
    /// <b>One window, not one prompt per argument.</b> The game asks for a rotor's arguments one
    /// after another, and that is the part of its behaviour the product owner asked not to copy.
    /// A form is more code than a sequence of single-value dialogs — it needs a focus, a layout
    /// and a "which field is wrong" answer — but it is the only shape in which the player can see
    /// the request as a whole before committing to it, and the only one that can say *which* of
    /// three values was refused.
    ///
    /// <b>Why this is in the model layer.</b> What counts as a valid argument, and which field
    /// broke, are decisions — not drawing. They are asserted by <c>tools/offline-check.ps1</c>,
    /// which is the only gate that can look at this without starting the game. The screen keeps
    /// the rectangle of each field and the keystrokes.
    ///
    /// <b>The editing state is one <see cref="TextEditor"/> per field.</b> A single editor shared
    /// between fields would lose the caret and the selection of the field the player came back
    /// to — and, worse, a paste into one field would land in whichever field was edited last.
    /// Fields are few (one to four in the whole game) and allocated once per dialog.
    /// </summary>
    public sealed class ParameterForm
    {
        /// <summary>Upper bound on the length of one value, in characters.</summary>
        public const int MaxValueLength = 64;

        private readonly ParameterSpec[] _specs;
        private readonly TextEditor[] _fields;
        private readonly string[] _labels;
        private readonly string[] _hints;
        private readonly string _actionCaption;

        private int _focus;
        private ParameterError _error;
        private int _errorIndex = -1;

        /// <summary>
        /// Builds a form for the given argument types without knowing which action they belong to:
        /// every field is taken at face value, as the game declared it. Used where no action key is
        /// available, and by the offline gate.
        /// </summary>
        public ParameterForm(IList<TypeCode> types, ParameterSet initial)
            : this(null, null, VgsLanguage.English, types, initial)
        {
        }

        /// <summary>
        /// Builds the form of one action's arguments, pre-filled from <paramref name="initial"/>
        /// where it has values and from the argument's own default where it does not.
        ///
        /// <b>An edit starts from what the connection already passes</b>, so "change one number"
        /// does not mean retyping the other two. <b>A new connection starts from the argument's
        /// default</b> — the value the game itself prefills, or the block's current state — because
        /// a rotor asked to "rotate to" something is almost always asked to rotate to a value near
        /// where it is now (ADR-0051).
        ///
        /// <b>Why the action key matters.</b> It is what selects the specification
        /// (<see cref="ActionParameterHints"/>): without it the form would believe the game's
        /// declared types, which for the built-in actions are placeholders — the defect ADR-0051
        /// fixes. A key the table does not describe is not an error: the fields fall back to the
        /// declared types, exactly as before.
        /// </summary>
        public ParameterForm(string actionKey, string actionCaption, VgsLanguage language,
                             IList<TypeCode> types, ParameterSet initial)
        {
            _actionCaption = actionCaption;

            // The specifications are asked for once, as a set: `For` is the only entry point that
            // refines them, and calling it per field would both allocate the whole array n times
            // and invite a second refinement at the call site.
            //
            // <b>The form's size is the number of dialogs, not the number of parameters.</b> The
            // game asks one question per dialog and a dialog does not always fill one parameter —
            // a colour is three integers behind one picker — so `For` decides how many fields
            // there are and the arrays are sized from its answer (ADR-0052 §2).
            ParameterSpec[] specs = ActionParameterHints.For(actionKey, types, language);
            int count = specs.Length;

            _specs = new ParameterSpec[count];
            _fields = new TextEditor[count];
            _labels = new string[count];
            _hints = new string[count];

            int slot = 0;
            for (int i = 0; i < count; i++)
            {
                ParameterSpec spec = specs[i];
                _specs[i] = spec;
                _fields[i] = new TextEditor(MaxValueLength);
                _labels[i] = string.Empty;
                _hints[i] = string.Empty;

                // The field's starting text is the values it stands for, in the game's own order:
                // one value for an ordinary field, several joined by commas for a field that
                // fills several parameters (a colour is three integers behind one picker).
                string start = null;
                if (initial != null && slot + spec.Slots <= initial.Count)
                {
                    start = JoinValues(initial, slot, spec.Slots, spec.Type);
                }

                if (start != null) _fields[i].SetText(start);
                else if (spec.DefaultFrom == ParameterDefault.Literal && spec.DefaultText != null)
                    _fields[i].SetText(spec.DefaultText);

                slot += spec.Slots;
            }
        }

        /// <summary>
        /// The game's own caption of the action, which is also the caption of the dialog it shows
        /// for the argument when there is exactly one (ADR-0052 §2). The screen uses it as the
        /// field's name when nothing better exists.
        /// </summary>
        public string ActionCaption
        {
            get { return _actionCaption; }
        }

        /// <summary>How many arguments the form asks for. Never zero for a form that exists.</summary>
        public int Count
        {
            get { return _specs.Length; }
        }

        /// <summary>Index of the field that has the keyboard.</summary>
        public int Focus
        {
            get { return _focus; }
        }

        /// <summary>
        /// The refusal of the last <see cref="TryBuild"/>, or <see cref="ParameterError.None"/>.
        /// Kept so the screen can mark the field that was wrong instead of only saying that
        /// something was.
        /// </summary>
        public ParameterError Error
        {
            get { return _error; }
        }

        /// <summary>Index of the field the last refusal was about, or -1.</summary>
        public int ErrorIndex
        {
            get { return _errorIndex; }
        }

        /// <summary>
        /// The type the field's value is stored as — the real one, not necessarily the one the game
        /// declared (ADR-0051).
        /// </summary>
        public TypeCode TypeAt(int index)
        {
            if (index < 0 || index >= _specs.Length) return TypeCode.Empty;
            return _specs[index].Type;
        }

        /// <summary>What the field accepts: its type, name, default and allowed values.</summary>
        public ParameterSpec SpecAt(int index)
        {
            if (index < 0 || index >= _specs.Length) return null;
            return _specs[index];
        }

        /// <summary>The text currently in one field, as typed.</summary>
        public string ValueAt(int index)
        {
            if (index < 0 || index >= _fields.Length) return string.Empty;
            return _fields[index].Text;
        }

        /// <summary>The editing state of one field, for the caret and the selection.</summary>
        public TextEditor EditorAt(int index)
        {
            if (index < 0 || index >= _fields.Length) return null;
            return _fields[index];
        }

        /// <summary>
        /// A caller-supplied label of one field — the screen passes a translated name such as
        /// "Angle". Empty when the caller has none, in which case the screen numbers the fields.
        ///
        /// The label lives here rather than in the form's construction because it is translated
        /// text, and the model layer holds no strings (ADR-0033).
        /// </summary>
        public string LabelAt(int index)
        {
            if (index < 0 || index >= _labels.Length) return string.Empty;
            return _labels[index];
        }

        /// <summary>Gives one field its label.</summary>
        public void SetLabel(int index, string label)
        {
            if (index < 0 || index >= _labels.Length) return;
            _labels[index] = label ?? string.Empty;
        }

        /// <summary>
        /// The line under one field saying what it accepts — the allowed values of a fixed list,
        /// for instance. Empty when there is nothing useful to add.
        ///
        /// Kept beside the label and for the same reason: it is translated text, and the model
        /// layer holds no strings (ADR-0033).
        /// </summary>
        public string HintAt(int index)
        {
            if (index < 0 || index >= _hints.Length) return string.Empty;
            return _hints[index];
        }

        /// <summary>Gives one field its hint line.</summary>
        public void SetHint(int index, string hint)
        {
            if (index < 0 || index >= _hints.Length) return;
            _hints[index] = hint ?? string.Empty;
        }

        /// <summary>Moves the keyboard to one field, clamped into the form.</summary>
        public void FocusAt(int index)
        {
            if (_specs.Length == 0) { _focus = 0; return; }
            if (index < 0) index = 0;
            if (index >= _specs.Length) index = _specs.Length - 1;
            _focus = index;
        }

        /// <summary>Moves the keyboard to the next field, wrapping.</summary>
        public void FocusNext()
        {
            if (_specs.Length == 0) return;
            _focus = (_focus + 1) % _specs.Length;
        }

        /// <summary>Moves the keyboard to the previous field, wrapping.</summary>
        public void FocusPrevious()
        {
            if (_specs.Length == 0) return;
            _focus = (_focus + _specs.Length - 1) % _specs.Length;
        }

        /// <summary>True while the focused field has a selection, for the screen's caret drawing.</summary>
        public bool FocusHasSelection
        {
            get
            {
                TextEditor editor = EditorAt(_focus);
                return editor != null && editor.HasSelection;
            }
        }

        /// <summary>
        /// Reads the form as a request. False when any field cannot hold its declared type, and
        /// then <see cref="Error"/> and <see cref="ErrorIndex"/> name the first bad field — the
        /// one the player should be looking at.
        ///
        /// <b>First bad field, not all of them.</b> The form is a sequence of values the action
        /// consumes in order; reporting the first one that is wrong is what lets the screen put
        /// the caret there and the player fix it, rather than handing back a list they have to
        /// work through.
        /// </summary>
        public bool TryBuild(out ParameterSet set)
        {
            set = ParameterSet.None;
            _error = ParameterError.None;
            _errorIndex = -1;

            if (_specs.Length == 0) return true;

            int slots = 0;
            for (int i = 0; i < _specs.Length; i++) slots += _specs[i].Slots;

            var items = new ActionParameter[slots];
            int at = 0;
            for (int i = 0; i < _specs.Length; i++)
            {
                ParameterSpec spec = _specs[i];
                ParameterError error;

                if (spec.Slots == 1)
                {
                    ActionParameter one;
                    if (!spec.TryValidate(_fields[i].Text, out one, out error))
                    {
                        _error = error;
                        _errorIndex = i;
                        _focus = i;
                        return false;
                    }
                    items[at++] = one;
                    continue;
                }

                // One field, several parameters — a colour is three integers behind one picker.
                // The values are separated by commas, and the count is checked first: a wrong
                // count is a wrong value, not a value that happens to be short.
                string[] parts = SplitValues(_fields[i].Text, spec.Slots);
                if (parts == null)
                {
                    _error = ParameterError.Invalid;
                    _errorIndex = i;
                    _focus = i;
                    return false;
                }

                for (int k = 0; k < spec.Slots; k++)
                {
                    ActionParameter one;
                    if (!spec.TryValidate(parts[k], out one, out error))
                    {
                        _error = error;
                        _errorIndex = i;
                        _focus = i;
                        return false;
                    }
                    items[at++] = one;
                }
            }

            set = ParameterSet.Of(items);
            return true;
        }

        /// <summary>
        /// The text a field starts with, taken from the values the toolbar item already holds, or
        /// null when any of them is not of the field's type (an item written by an older build, or
        /// by the game itself with a placeholder type).
        /// </summary>
        private static string JoinValues(ParameterSet set, int start, int count, TypeCode type)
        {
            if (count == 1)
            {
                ActionParameter one = set[start];
                return one.Code == type ? one.Text : null;
            }

            var parts = new string[count];
            for (int k = 0; k < count; k++)
            {
                ActionParameter one = set[start + k];
                if (one.Code != type) return null;
                parts[k] = one.Text;
            }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Splits a multi-value field into exactly <paramref name="count"/> values, or null when
        /// the text does not hold that many. Empty pieces are refused: "1,,3" is three pieces and
        /// two values, and treating the gap as zero would write a colour the player never chose.
        /// </summary>
        private static string[] SplitValues(string text, int count)
        {
            if (text == null) return null;

            string[] parts = text.Split(',');
            if (parts.Length != count) return null;

            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
                if (parts[i].Length == 0) return null;
            }
            return parts;
        }
    }
}








