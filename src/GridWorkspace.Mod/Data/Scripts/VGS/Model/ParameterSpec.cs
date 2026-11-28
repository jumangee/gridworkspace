using System;
using System.Collections.Generic;
using System.Globalization;

namespace Vgs.Model
{
    /// <summary>What a field accepts (ADR-0052).</summary>
    public enum ParameterKind
    {
        /// <summary>Free text, passed to the action unchanged.</summary>
        Text,

        /// <summary>A fractional number.</summary>
        Number,

        /// <summary>A whole number.</summary>
        WholeNumber,

        /// <summary>One of a fixed list of values; the player types the value, the window shows what it means.</summary>
        Choice
    }

    /// <summary>Where a field's starting value comes from, when the form opens (ADR-0052).</summary>
    public enum ParameterDefault
    {
        /// <summary>Nothing: the field starts empty.</summary>
        None,

        /// <summary>A literal in the specification.</summary>
        Literal,

        /// <summary>The programmable block's last run argument.</summary>
        ProgrammableBlockArgument,

        /// <summary>The rotor's current target velocity, in RPM.</summary>
        MotorVelocity,

        /// <summary>The rotor's lower limit, in degrees.</summary>
        MotorLowerLimit,

        /// <summary>The rotor's upper limit, in degrees.</summary>
        MotorUpperLimit
    }

    /// <summary>
    /// One argument of an action: the type its value is stored as, what the field accepts, the
    /// name the game's own dialog uses, what it starts at and which values are allowed — ADR-0052.
    ///
    /// <b>Why this exists at all.</b> The game publishes <c>ParameterDefinitions</c>, and for the
    /// built-in actions that list is a placeholder: a rotor's <c>RotateToAngle</c> declares three
    /// <c>String</c> arguments, while its own handler reads <c>Single</c>, <c>Single</c> and
    /// <c>Int64</c> and does nothing at all for anything else. Verified by IL, see
    /// <c>docs/01-research/api-action-parameters.md</c> §2.1. Writing an item with the declared
    /// type therefore produces a connection that exists in the toolbar and never fires.
    ///
    /// <b>Immutable and cheap to copy.</b> The refinements below return a new specification rather
    /// than mutating one, because the base row is shared by every block that offers the action and
    /// a per-block default must not leak into it.
    /// </summary>
    public sealed class ParameterSpec
    {
        /// <summary>The type the value is written into the toolbar item as.</summary>
        public readonly TypeCode Type;

        /// <summary>What the field accepts.</summary>
        public readonly ParameterKind Kind;

        /// <summary>
        /// The name of the argument — the caption of the dialog the game shows for it, or null when
        /// the game names nothing and the screen falls back to the action's own caption.
        /// </summary>
        public readonly string Name;

        /// <summary>Where the starting value comes from.</summary>
        public readonly ParameterDefault DefaultFrom;

        /// <summary>The literal starting value, when <see cref="DefaultFrom"/> is <see cref="ParameterDefault.Literal"/>.</summary>
        public readonly string DefaultText;

        /// <summary>True when the value must stay within <see cref="Min"/> and <see cref="Max"/>.</summary>
        public readonly bool HasRange;

        /// <summary>Lowest accepted value, inclusive.</summary>
        public readonly float Min;

        /// <summary>Highest accepted value, inclusive.</summary>
        public readonly float Max;

        /// <summary>The accepted values of a <see cref="ParameterKind.Choice"/> field, as the player types them.</summary>
        public readonly string[] Choices;

        /// <summary>Keys of the meaning of each entry in <see cref="Choices"/>, same order.</summary>
        public readonly string[] ChoiceKeys;

        /// <summary>
        /// How many toolbar parameters this one field fills.
        ///
        /// <b>Not always one.</b> The game asks one question per dialog, and a dialog does not
        /// always fill one parameter: <c>SetColor</c> declares three integer parameters but shows a
        /// single colour picker titled "Set Color", and writes all three from it. Rendering three
        /// fields there would force two of them to be invented names — exactly the placeholder this
        /// decision forbids — so the form follows the dialogs: one field, three values
        /// (ADR-0052 §2).
        /// </summary>
        public readonly int Slots;

        private ParameterSpec(TypeCode type, ParameterKind kind, string name,
                              ParameterDefault defaultFrom, string defaultText,
                              bool hasRange, float min, float max,
                              string[] choices, string[] choiceKeys, int slots)
        {
            Type = type;
            Kind = kind;
            Name = name;
            DefaultFrom = defaultFrom;
            DefaultText = defaultText;
            HasRange = hasRange;
            Min = min;
            Max = max;
            Choices = choices;
            ChoiceKeys = choiceKeys;
            Slots = slots < 1 ? 1 : slots;
        }

        /// <summary>Free text.</summary>
        public static ParameterSpec Text(string name, ParameterDefault defaultFrom)
        {
            return new ParameterSpec(TypeCode.String, ParameterKind.Text, name, defaultFrom, null,
                                     false, 0f, 0f, null, null, 1);
        }

        /// <summary>A fractional number, optionally bounded.</summary>
        public static ParameterSpec Number(string name, ParameterDefault defaultFrom,
                                           bool hasRange, float min, float max)
        {
            return new ParameterSpec(TypeCode.Single, ParameterKind.Number, name, defaultFrom, null,
                                     hasRange, min, max, null, null, 1);
        }

        /// <summary>A whole number, optionally bounded.</summary>
        public static ParameterSpec WholeNumber(string name, ParameterDefault defaultFrom,
                                                bool hasRange, long min, long max)
        {
            return new ParameterSpec(TypeCode.Int64, ParameterKind.WholeNumber, name, defaultFrom,
                                     null, hasRange, min, max, null, null, 1);
        }

        /// <summary>
        /// A specification of an exact type. Used where the type is known precisely — a channel is
        /// an <c>Int32</c> because its handler unboxes an <c>Int32</c>, and writing an <c>Int64</c>
        /// there would produce a value the handler cannot read (ADR-0052 §3).
        /// </summary>
        public static ParameterSpec Typed(TypeCode type, ParameterKind kind, string name,
                                         ParameterDefault defaultFrom)
        {
            return new ParameterSpec(type, kind, name, defaultFrom, null, false, 0f, 0f, null, null, 1);
        }

        /// <summary>The same specification, bounded.</summary>
        public ParameterSpec WithRange(float min, float max)
        {
            return new ParameterSpec(Type, Kind, Name, DefaultFrom, DefaultText, true, min, max,
                                     Choices, ChoiceKeys, Slots);
        }

        /// <summary>The same specification with a literal starting value.</summary>
        public ParameterSpec WithDefault(string text)
        {
            return new ParameterSpec(Type, Kind, Name, ParameterDefault.Literal, text,
                                     HasRange, Min, Max, Choices, ChoiceKeys, Slots);
        }

        /// <summary>The same specification starting from a value the block supplies.</summary>
        public ParameterSpec WithDefault(ParameterDefault from)
        {
            return new ParameterSpec(Type, Kind, Name, from, null,
                                     HasRange, Min, Max, Choices, ChoiceKeys, Slots);
        }

        /// <summary>The same specification stored as another type.</summary>
        public ParameterSpec WithType(TypeCode type)
        {
            return new ParameterSpec(type, Kind, Name, DefaultFrom, DefaultText, HasRange, Min, Max,
                                     Choices, ChoiceKeys, Slots);
        }

        /// <summary>The same specification filling more than one toolbar parameter.</summary>
        public ParameterSpec WithSlots(int slots)
        {
            return new ParameterSpec(Type, Kind, Name, DefaultFrom, DefaultText, HasRange, Min, Max,
                                     Choices, ChoiceKeys, slots);
        }

        /// <summary>
        /// The same specification, restricted to a fixed list. <paramref name="keys"/> are the text
        /// keys of each value's meaning, in the same order, or null when the values speak for
        /// themselves.
        /// </summary>
        public ParameterSpec AsChoice(string[] choices, string[] keys, string literalDefault)
        {
            return new ParameterSpec(TypeCode.Int64, ParameterKind.Choice, Name,
                                     ParameterDefault.Literal, literalDefault,
                                     false, 0f, 0f, choices, keys, Slots);
        }

        /// <summary>
        /// What the game declared, taken at face value: the only thing to go on for an action
        /// nobody described. No name and no default, because neither was published.
        /// </summary>
        public static ParameterSpec FromDeclared(TypeCode code)
        {
            switch (code)
            {
                case TypeCode.Boolean:
                    return new ParameterSpec(TypeCode.Boolean, ParameterKind.Text, null,
                                             ParameterDefault.None, null, false, 0f, 0f, null, null, 1);

                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    return new ParameterSpec(code, ParameterKind.Number, null,
                                             ParameterDefault.None, null, false, 0f, 0f, null, null, 1);

                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return new ParameterSpec(code, ParameterKind.WholeNumber, null,
                                             ParameterDefault.None, null, false, 0f, 0f, null, null, 1);

                default:
                    return new ParameterSpec(TypeCode.String, ParameterKind.Text, null,
                                             ParameterDefault.None, null, false, 0f, 0f, null, null, 1);
            }
        }

        /// <summary>True when this field's value is one of a fixed list.</summary>
        public bool IsChoice
        {
            get { return Kind == ParameterKind.Choice && Choices != null && Choices.Length > 0; }
        }

        /// <summary>
        /// Reads one field. False when the text is not a value this argument can take, and then
        /// <paramref name="error"/> says which kind of wrong it is: unreadable, out of the allowed
        /// range, or not one of the listed values.
        ///
        /// <b>This is the check that keeps a dead connection out of the toolbar.</b> The write
        /// happens only after every field has passed (ADR-0050 §4, ADR-0052 §3).
        /// </summary>
        public bool TryValidate(string text, out ActionParameter parameter, out ParameterError error)
        {
            if (!ActionParameter.TryCreate(Type, text, out parameter, out error)) return false;

            if (IsChoice && !IsAllowedChoice(parameter.Text))
            {
                error = ParameterError.NotAllowed;
                return false;
            }

            if (HasRange && Kind != ParameterKind.Text)
            {
                double value;
                if (double.TryParse(parameter.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                                    out value)
                    && (value < Min || value > Max))
                {
                    error = ParameterError.OutOfRange;
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when <paramref name="text"/> is one of the listed values.</summary>
        public bool IsAllowedChoice(string text)
        {
            if (Choices == null || text == null) return false;

            for (int i = 0; i < Choices.Length; i++)
            {
                if (string.Equals(Choices[i], text, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// The text-table key of the meaning of a value, or null when the value is not one of the
        /// listed ones. This is what turns "2" into "counter-clockwise" on screen.
        /// </summary>
        public string ChoiceKeyOf(string text)
        {
            if (Choices == null || ChoiceKeys == null || text == null) return null;

            for (int i = 0; i < Choices.Length && i < ChoiceKeys.Length; i++)
            {
                if (string.Equals(Choices[i], text, StringComparison.Ordinal)) return ChoiceKeys[i];
            }
            return null;
        }

        /// <summary>How many values the list holds.</summary>
        public int ChoiceCount
        {
            get { return Choices == null ? 0 : Choices.Length; }
        }

        /// <summary>The value at one position of the list, or null.</summary>
        public string ChoiceAt(int index)
        {
            if (Choices == null || index < 0 || index >= Choices.Length) return null;
            return Choices[index];
        }

        /// <summary>The meaning key at one position of the list, or null.</summary>
        public string ChoiceKeyAt(int index)
        {
            if (ChoiceKeys == null || index < 0 || index >= ChoiceKeys.Length) return null;
            return ChoiceKeys[index];
        }
    }
}



