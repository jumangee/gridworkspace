using System;
using System.Collections.Generic;
using VRageMath;

namespace Vgs.Model
{
    /// <summary>
    /// What a port represents. The distinction matters for the evaluator (stage 3): an
    /// action is something you *invoke* on a block, an event is something that *happened*,
    /// and a value is something you read and compare.
    /// </summary>
    public enum PortKind
    {
        /// <summary>Terminal action, e.g. OnOff, TriggerNow. Consumed by a block.</summary>
        Action = 0,

        /// <summary>Something the block emits: state change, threshold crossing, occupancy.</summary>
        Event = 1,

        /// <summary>Readable scalar, e.g. integrity percentage, stored power.</summary>
        Value = 2
    }

    /// <summary>
    /// Direction of data flow for a port.
    ///
    /// This is the side of the node the port lives on: an <see cref="Input"/> is drawn as a
    /// bar under the card with its dot on the left, an <see cref="Output"/> as a bar with its
    /// dot on the right. Data always flows output → input, so a line always starts at a dot
    /// on the right of a source bar and ends at a dot on the left of a target bar.
    /// </summary>
    public enum PortDirection
    {
        Input = 0,
        Output = 1
    }

    /// <summary>
    /// Which fire group of the source block's toolbar an outgoing port writes into.
    ///
    /// Space Engineers toolbars are not all "everything fires at once". Two shapes exist,
    /// and the game decides which one a block has (see <see cref="ToolbarLayout.YesNo"/>):
    ///
    ///  * a <b>single-group</b> toolbar (a timer: nine slots × ten pages) — every filled cell
    ///    is activated together when the block fires;
    ///  * a <b>yes/no</b> toolbar (a sensor, an event controller, a cockpit's "on lock"
    ///    toolbar: two slots × ten pages) — slot 0 of every page fires on "yes", slot 1 on
    ///    "no". The two groups are independent lists of actions.
    ///
    /// A yes/no block therefore exposes *two* ports per event: "Detected (yes)" and
    /// "Detected (no)". <see cref="Single"/> is the only group of a single-group toolbar and
    /// is never used on a yes/no one.
    /// </summary>
    public enum PortGroup
    {
        /// <summary>The block's toolbar has one fire group; every cell fires together.</summary>
        Single = 0,

        /// <summary>Slot 0 of every page: the "yes" / "true" / "detected" list.</summary>
        Yes = 1,

        /// <summary>Slot 1 of every page: the "no" / "false" / "not detected" list.</summary>
        No = 2
    }

    /// <summary>
    /// One connectable point on a node.
    ///
    /// <see cref="Key"/> is the stable identity used in save data and in the graph; it must
    /// never be renumbered, only added to. <see cref="DisplayName"/> is what the user sees.
    ///
    /// <b>An action port may declare arguments</b> (<see cref="ParameterTypes"/>). That is a fact
    /// about the block's type — a programmable block's <c>Run</c> always takes a string — read
    /// from the game when the port list is built, never guessed from the subtype. The *values*
    /// are not here: they belong to a connection, because two connections may ask the same action
    /// for different things (ADR-0050, <see cref="InputBar"/>).
    ///
    /// <see cref="Anchor"/> and <see cref="Slot"/> are two different things on purpose:
    ///
    ///  * <see cref="Anchor"/> is a *derived* position on the card edge, kept only so the
    ///    catalog can spread a long port list down the side of the card. It is never used to
    ///    place a connection dot — dots live on the port bars, not on the card.
    ///  * <see cref="Slot"/> is the port's *drawing slot*: which stacked bar under the card it
    ///    occupies. It is assigned by <see cref="GridGraph.LayoutPorts"/>, not by the catalog,
    ///    because it depends on what the user has actually connected. Two nodes can therefore
    ///    place the same port in different slots, and a port that was not drawn yesterday may
    ///    be drawn today.
    ///
    /// Everything except <see cref="Slot"/> is fixed for the lifetime of the block; the slot
    /// is rewritten only by the layout pass, and only as a whole (see the pass for why a
    /// half-updated slot map would draw two ports on top of each other).
    /// </summary>
    public sealed class Port
    {
        public readonly string Key;
        public readonly string DisplayName;
        public readonly PortKind Kind;
        public readonly PortDirection Direction;

        /// <summary>
        /// Which fire group of the source's toolbar this port belongs to. Always
        /// <see cref="PortGroup.Single"/> for an input port: actions have no groups.
        /// </summary>
        public readonly PortGroup Group;

        /// <summary>Normalized vertical position on the node edge (0 = top, 1 = bottom).</summary>
        public readonly float Anchor;

        /// <summary>
        /// Drawing slot: index of the stacked port bar this port occupies under its card,
        /// counted from the card outwards. Negative means "not drawn" (no connection, so the
        /// bar would be noise). Assigned by <see cref="GridGraph.LayoutPorts"/>.
        ///
        /// <b>An event port's bar is this slot. An action port's bar is not.</b> Since an action
        /// can be asked with arguments, one action port may need several bars — one per distinct
        /// request — and a single integer cannot describe that. The input stack is therefore
        /// built from <see cref="Node.InputBars"/>, each carrying its own
        /// <see cref="InputBar.Slot"/>; this field stays -1 for every action port, and the
        /// geometry of an input bar is asked of <c>PortGeometry.TryInputBarRect</c> with the bar
        /// rather than with the port (ADR-0050).
        /// </summary>
        public int Slot; // review:allow R009 assigned only by GridGraph.LayoutPorts, for the whole node at once

        /// <summary>
        /// The types of the arguments this action requires, in the order the game asks for them,
        /// or an empty list for an action that takes none. <b>Read from the game</b>
        /// (<c>MyToolbarApiExtensions.GetActionParameterDefinitions</c>) when the port list is
        /// built — it is a property of the block's type, not of an instance, and it is the only
        /// thing the game publishes about arguments: types, never bounds (ADR-0050,
        /// <c>docs/01-research/api-action-parameters.md</c>).
        ///
        /// Always non-null, so a caller never has to ask whether arguments were read before it
        /// can ask how many there are.
        /// </summary>
        public readonly IList<TypeCode> ParameterTypes;

        private static readonly TypeCode[] NoParameters = new TypeCode[0];

        /// <summary>True when the action cannot run without arguments, i.e. the editor must ask for them.</summary>
        public bool HasParameters
        {
            get { return ParameterTypes != null && ParameterTypes.Count > 0; }
        }

        public Port(string key, string displayName, PortKind kind, PortDirection direction, float anchor)
            : this(key, displayName, kind, direction, anchor, PortGroup.Single)
        {
        }

        public Port(string key, string displayName, PortKind kind, PortDirection direction,
                    float anchor, PortGroup group)
            : this(key, displayName, kind, direction, anchor, group, null)
        {
        }

        /// <summary>
        /// The full form, with the argument types the game reports for this action.
        /// <paramref name="parameterTypes"/> may be null, which means "this action takes none" —
        /// the case of every event port and of the actions of most blocks.
        /// </summary>
        public Port(string key, string displayName, PortKind kind, PortDirection direction,
                    float anchor, PortGroup group, IList<TypeCode> parameterTypes)
        {
            Key = key;
            DisplayName = displayName;
            Kind = kind;
            Direction = direction;
            Anchor = anchor;
            Group = group;
            ParameterTypes = parameterTypes ?? NoParameters;
            Slot = -1;
        }

        public override string ToString()
        {
            return Direction + ":" + Key;
        }
    }

}
