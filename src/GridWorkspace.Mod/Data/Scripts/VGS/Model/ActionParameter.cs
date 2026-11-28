using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Vgs.Model
{
    /// <summary>
    /// Why a typed value was refused as an action parameter. The three answers are told apart
    /// because the player's next move differs: a word in a number field is a typo, a number the
    /// type cannot hold is a different number, and neither is "the game said no".
    /// </summary>
    public enum ParameterError
    {
        /// <summary>The value was accepted.</summary>
        None = 0,

        /// <summary>The text cannot be read as the parameter's type at all ("abc" for a number).</summary>
        Invalid = 1,

        /// <summary>The text is a number, but outside the range the type can hold.</summary>
        OutOfRange = 2,

        /// <summary>
        /// The value is a number of the right type, but not one of the values this argument
        /// accepts — a rotor direction of 7, when only 0, 1 and 2 mean anything (ADR-0051).
        /// </summary>
        NotAllowed = 3
    }

    /// <summary>
    /// One argument of a terminal action: what the game calls a
    /// <c>TerminalActionParameter</c>, reduced to the two things a mod can know about it —
    /// its <see cref="TypeCode"/> and its value <b>as the text the game itself would save</b>.
    ///
    /// <b>Why this type exists at all.</b> Some actions do not do anything on their own: a
    /// programmable block's <c>Run</c> needs the argument to pass to the script, a rotor's
    /// <c>RotateToAngle</c> needs an angle, a speed and a direction. The game asks for those
    /// values when the player drops such an action into a toolbar, and stores them <em>inside
    /// the toolbar item</em>. A connection that ignored them would be a connection that does
    /// nothing, so the editor has to carry them too (ADR-0050).
    ///
    /// <b>Why the value is a string and not an <c>object</c>.</b> The game's own serialised form
    /// is a string plus a type code (<c>MyObjectBuilder_ToolbarItemActionParameter</c>), and that
    /// is what a save file holds. Keeping the same form here means "the connection the editor
    /// drew" and "the connection the game saved" are compared by one rule — ordinal text
    /// equality — instead of by a boxing comparison that would have to know every primitive.
    /// The canonical text is produced by the same conversion the game uses
    /// (<c>Convert.ToString(value, InvariantCulture)</c>), so a value the editor wrote and the
    /// same value read back from the toolbar compare equal (see
    /// <c>docs/01-research/api-action-parameters.md</c>).
    ///
    /// <b>Immutable, and a struct.</b> A parameter is two words; a connection carries a handful
    /// of them, and a refresh reads the parameters of every scanned cell. A class per parameter
    /// would be an allocation per cell per pass for something that never changes.
    /// </summary>
    public struct ActionParameter
    {
        /// <summary>
        /// The type the value has, exactly as the game reports it
        /// (<c>TerminalActionParameter.TypeCode</c>). <see cref="TypeCode.String"/> is the one
        /// type whose value is used verbatim; every other type is a number or a boolean and is
        /// stored in its canonical invariant text.
        /// </summary>
        public readonly TypeCode Code;

        /// <summary>
        /// The value in the game's own serialised form: verbatim for a string, canonical
        /// invariant text for everything else. Never null — an empty string is a real value
        /// (a programmable block run with no argument is a legitimate request).
        /// </summary>
        public readonly string Text;

        private ActionParameter(TypeCode code, string text)
        {
            Code = code;
            Text = text;
        }

        /// <summary>
        /// True for the value that is not a value: <see cref="TypeCode.Empty"/> is what the
        /// game's own <c>TerminalActionParameter.Empty</c> carries, and the game refuses to
        /// deserialise a parameter whose code is that low (its
        /// <c>AssertTypeCodeValidity</c> throws for <c>TypeCode</c> ≤ 2). A parameter that is
        /// empty is not written to a toolbar item at all.
        /// </summary>
        public bool IsEmpty
        {
            get { return Code == TypeCode.Empty; }
        }

        /// <summary>
        /// True when the value is free text, i.e. the field accepts anything the player types.
        /// A number or a boolean field filters its characters instead (see
        /// <see cref="IsNumeric"/>), because a letter there can only be a typo.
        /// </summary>
        public bool IsTextual
        {
            get { return Code == TypeCode.String; }
        }

        /// <summary>
        /// True when the value is a number, which is the only case where the player can type
        /// something that is well-formed but out of range.
        /// </summary>
        public bool IsNumeric
        {
            get
            {
                switch (Code)
                {
                    case TypeCode.SByte:
                    case TypeCode.Byte:
                    case TypeCode.Int16:
                    case TypeCode.UInt16:
                    case TypeCode.Int32:
                    case TypeCode.UInt32:
                    case TypeCode.Int64:
                    case TypeCode.UInt64:
                    case TypeCode.Single:
                    case TypeCode.Double:
                    case TypeCode.Decimal:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Reads the player's text as a value of the given type, and canonicalises it.
        ///
        /// <b>This is the only place that decides whether typed text is a valid argument.</b>
        /// It is in the model layer on purpose: the dialog, the write path and the offline gate
        /// all ask the same question, and a second implementation of "is this a number" in the
        /// game layer is how the two would start disagreeing.
        ///
        /// <b>No exceptions, and that is not a style choice.</b> The obvious implementation is
        /// <c>Convert.ChangeType</c> inside a <c>try</c>, which is what the game itself does — but
        /// its overflow case throws <see cref="System.OverflowException"/>, and that type is
        /// <b>prohibited for mod scripts</b>: naming it in a <c>catch</c> made the whole mod fail
        /// to load with "The type or member 'OverflowException' is prohibited". So every type is
        /// parsed by its own <c>TryParse</c> in the invariant culture and its range checked
        /// explicitly. The answer is the same, and it is now checkable offline.
        ///
        /// <b>Range.</b> The check is the <em>type's</em> range, not the action's. The game
        /// publishes a parameter's type and nothing else — no minimum, no maximum, no step (see
        /// <c>docs/01-research/api-action-parameters.md</c> §2) — and the bounds a player sees in
        /// the vanilla dialog live in the block's own C# code, not in the metadata. Inventing a
        /// range from a subtype table is exactly the kind of guess ADR-0011 removed. What the
        /// game does with an out-of-range number is clamp it in the action's own body, so a
        /// number the type can hold is a number the action accepts.
        /// </summary>
        public static bool TryCreate(TypeCode code, string raw, out ActionParameter parameter,
                                     out ParameterError error)
        {
            parameter = default(ActionParameter);
            error = ParameterError.None;

            if (code == TypeCode.String)
            {
                parameter = new ActionParameter(code, raw ?? string.Empty);
                return true;
            }

            // Empty, Object and DBNull are not values a toolbar parameter can carry: the game's
            // own deserialiser throws on them, so a field of such a type could never be filled.
            if (code <= TypeCode.DBNull)
            {
                error = ParameterError.Invalid;
                return false;
            }

            string text = raw == null ? string.Empty : raw.Trim();
            if (text.Length == 0)
            {
                error = ParameterError.Invalid;
                return false;
            }

            string canonical;
            if (!TryCanonical(code, text, out canonical, out error)) return false;

            parameter = new ActionParameter(code, canonical);
            return true;
        }

        /// <summary>
        /// <summary>
        /// Parses and re-formats one value, or reports why it cannot be one. Split out of
        /// <see cref="TryCreate"/> only because it is long; it is the same rule.
        ///
        /// <b>Two kinds of refusal, told apart.</b> A word in a number field is a typo and the
        /// player retypes it; a number the type cannot hold is a different number. The
        /// distinction is recovered by asking whether the text is a number at all — which is also
        /// why nothing here throws: the game's own path lets an overflow escape as
        /// <c>OverflowException</c>, and naming that type in a <c>catch</c> is prohibited for mod
        /// scripts (it stopped this mod from loading).
        /// </summary>
        private static bool TryCanonical(TypeCode code, string text, out string canonical,
                                         out ParameterError error)
        {
            canonical = null;
            error = ParameterError.None;

            switch (code)
            {
                case TypeCode.Boolean:
                {
                    bool value;
                    if (!bool.TryParse(text, out value))
                    {
                        error = ParameterError.Invalid;
                        return false;
                    }
                    canonical = value ? bool.TrueString : bool.FalseString;
                    return true;
                }

                case TypeCode.Char:
                {
                    // One character, and not the empty string: the type holds exactly one.
                    if (text.Length != 1)
                    {
                        error = ParameterError.Invalid;
                        return false;
                    }
                    canonical = text;
                    return true;
                }

                case TypeCode.DateTime:
                {
                    DateTime value;
                    if (!DateTime.TryParse(text, CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out value))
                    {
                        error = ParameterError.Invalid;
                        return false;
                    }
                    canonical = value.ToString("o", CultureInfo.InvariantCulture);
                    return true;
                }
            }

            object value2;
            if (!TryParseNumber(code, text, out value2))
            {
                error = ClassifyFailure(code, text);
                return false;
            }

            canonical = Convert.ToString(value2, CultureInfo.InvariantCulture);
            if (canonical == null)
            {
                error = ParameterError.Invalid;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Reads the text as the exact type, in the invariant culture. False means "not a value
        /// of this type" — either malformed or outside its range, which the caller tells apart
        /// with <see cref="IsNumericText"/>.
        ///
        /// <b>Each type is parsed by its own <c>TryParse</c>, never through a wider one.</b> A
        /// <c>double</c> in the middle would round a <c>float</c> parameter differently from the
        /// game's own <c>Convert.ChangeType(text, TypeCode.Single)</c>, and the same value would
        /// then compare unequal to itself after a round trip through the toolbar.
        /// </summary>
        private static bool TryParseNumber(TypeCode code, string text, out object value)
        {
            value = null;

            const NumberStyles style = NumberStyles.Float;

            switch (code)
            {
                case TypeCode.SByte:
                {
                    sbyte v;
                    if (!sbyte.TryParse(text, style, CultureInfo.InvariantCulture, out v)) return false;
                    value = v;
                    return true;
                }
                case TypeCode.Byte:
                {
                    byte v;
                    if (!byte.TryParse(text, style, CultureInfo.InvariantCulture, out v)) return false;
                    value = v;
                    return true;
                }
                case TypeCode.Int16:
                {
                    short v;
                    if (!short.TryParse(text, style, CultureInfo.InvariantCulture, out v)) return false;
                    value = v;
                    return true;
                }
                case TypeCode.UInt16:
                {
                    ushort v;
                    if (!ushort.TryParse(text, style, CultureInfo.InvariantCulture, out v)) return false;
                    value = v;
                    return true;
                }
                case TypeCode.Int32:
                {
                    int v;
                    if (!int.TryParse(text, style, CultureInfo.InvariantCulture, out v)) return false;
                    value = v;
                    return true;
                }
                case TypeCode.UInt32:
                {
                    uint v;
                    if (!uint.TryParse(text, style, CultureInfo.InvariantCulture, out v)) return false;
                    value = v;
                    return true;
                }
                case TypeCode.Int64:
                {
                    long v;
                    if (!long.TryParse(text, style, CultureInfo.InvariantCulture, out v)) return false;
                    value = v;
                    return true;
                }
                case TypeCode.UInt64:
                {
                    ulong v;
                    if (!ulong.TryParse(text, style, CultureInfo.InvariantCulture, out v)) return false;
                    value = v;
                    return true;
                }
                case TypeCode.Single:
                {
                    float v;
                    if (!float.TryParse(text, style, CultureInfo.InvariantCulture, out v)) return false;

                    // A NaN or an infinity is a float, but not a value an action can act on:
                    // a rotor asked to turn to NaN is a connection that silently does nothing.
                    if (float.IsNaN(v) || float.IsInfinity(v)) return false;

                    value = v;
                    return true;
                }
                case TypeCode.Double:
                {
                    double v;
                    if (!double.TryParse(text, style, CultureInfo.InvariantCulture, out v)) return false;
                    if (double.IsNaN(v) || double.IsInfinity(v)) return false;
                    value = v;
                    return true;
                }
                case TypeCode.Decimal:
                {
                    decimal v;
                    if (!decimal.TryParse(text, style, CultureInfo.InvariantCulture, out v)) return false;
                    value = v;
                    return true;
                }
                default:
                    // A type the game's own parameter list does not carry.
                    return false;
            }
        }

        /// <summary>
        /// Why a numeric field refused its text: a typo, or a number that does not fit.
        ///
        /// <b>The distinction is not "does it look like a number".</b> A fractional value in an
        /// integer field (<c>45.5</c>) is a number, and it is still a typo — the field never takes
        /// fractions, so no range would make it right. The test is therefore: a number of the
        /// right <em>shape</em> that the type cannot hold is out of range, everything else is
        /// invalid. Getting this backwards is not cosmetic — the two sentences tell the player to
        /// do different things ("type a number" versus "that number is too big").
        /// </summary>
        private static ParameterError ClassifyFailure(TypeCode code, string text)
        {
            double wide;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out wide))
            {
                return ParameterError.Invalid;
            }

            if (IsIntegral(code))
            {
                // An integer field holds whole numbers only: a fraction is the wrong shape, not
                // a value out of range, however small it is.
                if (Math.Floor(wide) != wide) return ParameterError.Invalid;
                return ParameterError.OutOfRange;
            }

            return ParameterError.OutOfRange;
        }

        /// <summary>True for the type codes that hold whole numbers only.</summary>
        private static bool IsIntegral(TypeCode code)
        {
            switch (code)
            {
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return true;
                default:
                    return false;
            }
        }

        /// Builds a parameter from a value the game reported, canonicalising it the same way
        /// <see cref="TryCreate"/> does. Used when a cell of a toolbar is read back: the game
        /// hands over the boxed value it deserialised, and this is where it becomes text.
        /// </summary>
        public static ActionParameter FromValue(TypeCode code, object value)
        {
            if (value == null) return new ActionParameter(code, string.Empty);
            return new ActionParameter(code, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        /// <summary>
        /// The value as the player should see it: free text in quotes, so that the string
        /// <c>45</c> and the number <c>45</c> are told apart on a port bar.
        /// </summary>
        public string Describe()
        {
            if (IsEmpty) return string.Empty;
            if (IsTextual) return "\"" + Text + "\"";
            return Text;
        }

        public override string ToString()
        {
            return Code + ":" + Text;
        }
    }

    /// <summary>
    /// All the arguments of one action, as one value: the parameter list of a connection
    /// (ADR-0050).
    ///
    /// <b>Why a set and not a list on the edge.</b> Two connections that pass the same arguments
    /// to the same action are the same <em>request</em>, and the picture has to say so: they
    /// share one input bar and one line each to its dot. Two connections with different
    /// arguments are two requests and therefore two bars. Comparing them is what
    /// <see cref="SameAs"/> is for, and it is deliberately cheap — a signature string built once
    /// — because it runs for every edge of every card on every layout pass.
    ///
    /// <b>Immutable.</b> A different set of arguments is a different set: the graph holds it and
    /// replaces the edge, exactly as it does for any other field of a connection.
    /// </summary>
    public sealed class ParameterSet
    {
        /// <summary>The empty set: an action that takes no arguments, or one whose arguments are not known.</summary>
        public static readonly ParameterSet None = new ParameterSet(new ActionParameter[0]);

        private readonly ActionParameter[] _items;
        private string _signature;
        private string _display;

        private ParameterSet(ActionParameter[] items)
        {
            _items = items;
        }

        /// <summary>How many arguments the action takes. Zero for the ordinary case.</summary>
        public int Count
        {
            get { return _items.Length; }
        }

        /// <summary>True when there are no arguments at all.</summary>
        public bool IsEmpty
        {
            get { return _items.Length == 0; }
        }

        /// <summary>The argument at <paramref name="index"/>, in the action's own order.</summary>
        public ActionParameter this[int index]
        {
            get { return _items[index]; }
        }

        /// <summary>
        /// Copies the arguments into a set. The caller's list is not retained: a set is a value
        /// the graph keeps for the life of a connection, and a list the caller still owns could
        /// be cleared underneath it.
        /// </summary>
        public static ParameterSet Of(IList<ActionParameter> items)
        {
            if (items == null || items.Count == 0) return None;

            var copy = new ActionParameter[items.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = items[i];
            return new ParameterSet(copy);
        }

        /// <summary>
        /// A stable, unambiguous text form of the whole set, used to tell two sets apart.
        ///
        /// <b>Length-prefixed rather than delimiter-escaped.</b> An argument is arbitrary text —
        /// a script argument can contain commas, quotes and newlines — so any separator would
        /// need escaping, and an escaping bug would silently merge two different requests into
        /// one bar. A length prefix cannot be forged by the value it describes.
        /// </summary>
        public string Signature
        {
            get
            {
                if (_signature == null) _signature = BuildSignature();
                return _signature;
            }
        }

        /// <summary>
        /// The values as one line for a port bar: <c>"arg", 45, 10</c>. Built once, because the
        /// draw path reads it every frame.
        /// </summary>
        public string Display
        {
            get
            {
                if (_display == null) _display = BuildDisplay();
                return _display;
            }
        }

        /// <summary>
        /// True when both sets are the same request: same count, same types, same values. A null
        /// set means "no arguments", so <see cref="None"/> and null compare equal — that is what
        /// lets the read-back check of an unparameterised connection keep its old, one-argument
        /// shape.
        /// </summary>
        public bool SameAs(ParameterSet other)
        {
            if (other == null || other._items.Length == 0) return _items.Length == 0;
            if (_items.Length != other._items.Length) return false;

            for (int i = 0; i < _items.Length; i++)
            {
                if (_items[i].Code != other._items[i].Code) return false;
                if (!string.Equals(_items[i].Text, other._items[i].Text, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private string BuildSignature()
        {
            if (_items.Length == 0) return string.Empty;

            var sb = new StringBuilder(32);
            for (int i = 0; i < _items.Length; i++)
            {
                ActionParameter p = _items[i];
                string text = p.Text ?? string.Empty;
                sb.Append((int)p.Code).Append(':').Append(text.Length).Append(':').Append(text).Append(';');
            }
            return sb.ToString();
        }

        private string BuildDisplay()
        {
            if (_items.Length == 0) return string.Empty;

            var sb = new StringBuilder(24);
            for (int i = 0; i < _items.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(_items[i].Describe());
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            return "[" + Display + "]";
        }
    }
}
