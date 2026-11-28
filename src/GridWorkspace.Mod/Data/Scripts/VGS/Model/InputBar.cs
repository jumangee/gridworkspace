using System;

namespace Vgs.Model
{
    /// <summary>
    /// One drawn bar under a card: <b>one action, asked with one set of arguments</b>
    /// (ADR-0050).
    ///
    /// <b>Why the bar is not the port.</b> A port is a property of the block's type — "this block
    /// can be told to Run" — and it is unique per action. A bar is what the player sees, and once
    /// an action takes arguments there can be several <em>requests</em> of the same action on one
    /// card: a timer that runs a script with <c>scan</c> and another that runs it with
    /// <c>build</c> are two different things to ask. Drawing them as one bar would either hide
    /// one of the two argument sets or leave the player unable to tell which line carries which —
    /// and the bar's own "x" button would then delete both. So the bar carries the arguments, and
    /// two edges share a bar exactly when they ask for the same thing.
    ///
    /// <b>What the bar is not.</b> It is not state of the block and it is not saved: it is derived
    /// from the edges by <see cref="GridGraph.LayoutPorts"/>, which is also the only thing that
    /// gives it a <see cref="Slot"/>. A bar exists while at least one edge carries that request,
    /// and disappears with its last one — the same rule the port bars have always followed.
    ///
    /// <b>The label is built once.</b> It is the port's display name plus the arguments, and the
    /// draw path reads it for every visible card on every frame; building it per frame would
    /// allocate two strings per bar per frame.
    /// </summary>
    public sealed class InputBar
    {
        /// <summary>
        /// The port this bar is a request of. Its <see cref="Port.Key"/> is the action id the
        /// toolbar item carries, and its <see cref="Port.DisplayName"/> is the caption.
        /// </summary>
        public readonly Port Port;

        /// <summary>
        /// The arguments of this request. Never null; <see cref="ParameterSet.None"/> for an
        /// action that takes none.
        /// </summary>
        public readonly ParameterSet Parameters;

        /// <summary>
        /// The caption of the bar: the action's name, then its arguments —
        /// <c>Run · "scan"</c>, <c>Rotate To Angle · 45, 10, 1</c>. A bar whose action takes no
        /// arguments keeps the bare name it has always had.
        /// </summary>
        public readonly string Label;

        /// <summary>
        /// Drawing slot: index of the stacked bar this request occupies under its card, counted
        /// from the card outwards. Negative means "not drawn" — either the stack is full
        /// (<see cref="GridGraph.MaxPortsPerSide"/>) or the layout pass has not run. Assigned
        /// only by <see cref="GridGraph.LayoutPorts"/>, for the whole node at once.
        /// </summary>
        public int Slot; // review:allow R009 assigned only by GridGraph.LayoutPorts, for the whole node at once

        public InputBar(Port port, ParameterSet parameters)
        {
            Port = port;
            Parameters = parameters ?? ParameterSet.None;
            Slot = -1;
            Label = BuildLabel(port, Parameters);
        }

        /// <summary>
        /// True when this request actually carries values, which is when the caption shows them.
        /// </summary>
        public bool HasValues
        {
            get { return !Parameters.IsEmpty; }
        }

        /// <summary>
        /// True when the bar offers the edit button: the **action** declares arguments, so there
        /// is something a value could be set to.
        ///
        /// <b>Why this is the action's fact and not the request's.</b> A connection whose item
        /// carries no values — one wired in the vanilla terminal, or one written by a version of
        /// this mod that predates ADR-0050 — is a connection that cannot run its action. Its bar
        /// shows no values, but the pen must still be there, because filling them in is exactly
        /// what the player needs to do. Gating the button on the *request* would hide the only
        /// way to fix it.
        /// </summary>
        public bool CanEdit
        {
            get { return Port != null && Port.HasParameters; }
        }

        /// <summary>
        /// The one place the caption is composed. The separator is the same "·" the event ports
        /// use for their fire group (<see cref="BlockPortCatalog.EventDisplayName"/>): one mark
        /// for "the name, then a qualifier" on the whole canvas, and it is punctuation rather
        /// than text, so it is not translated (ADR-0033).
        /// </summary>
        private static string BuildLabel(Port port, ParameterSet parameters)
        {
            string name = port != null ? port.DisplayName : string.Empty;
            if (parameters.IsEmpty) return name;

            string values = parameters.Display;
            if (string.IsNullOrEmpty(values)) return name;

            return name + " · " + values;
        }

        public override string ToString()
        {
            return (Port != null ? Port.Key : "?") + " " + Parameters;
        }
    }
}
