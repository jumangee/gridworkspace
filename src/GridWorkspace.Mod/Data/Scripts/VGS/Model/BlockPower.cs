namespace Vgs.Model
{
    /// <summary>
    /// What the block's own on/off switch says right now (ADR-0035), as the card draws it.
    ///
    /// <b>Three states, and the third one is not "off".</b> A block that is switched off can be
    /// switched back on; a block damaged below its critical integrity cannot, and the game itself
    /// stops treating it as functional (<c>MyComponentStack.IsFunctional</c> is
    /// <c>IsBuilt &amp;&amp; Integrity &gt; MaxIntegrity * CriticalIntegrityRatio</c>, read from
    /// the game's IL). Those are two different answers to "can I switch this on", and the card
    /// says them in two different colours: grey for off, red for "you cannot switch it on".
    /// Collapsing them into one value would make a block that is merely switched off look broken,
    /// and a broken one look merely switched off.
    ///
    /// <b>Unknown is a state, not a default to be drawn.</b> A node exists before the first state
    /// pass reaches it, and the pass runs within half a second (and immediately after the
    /// structural pass that created the node). Drawing "off" in that window would be a claim
    /// about a block nobody has read yet — the same rule <see cref="BlockState.Integrity"/>
    /// follows with its "starts at 1" comment. So the switch is simply not drawn until the value
    /// is known, and because the column's width is decided by the block's *type* rather than by
    /// its state, nothing on the card moves when the icon appears.
    ///
    /// The numeric values are not persisted and not compared with anything but each other.
    /// </summary>
    public enum BlockPower
    {
        /// <summary>Not read yet. Draws nothing; the column keeps its width.</summary>
        Unknown = 0,

        /// <summary>The block is switched on.</summary>
        On = 1,

        /// <summary>The block is switched off, and switching it on is possible.</summary>
        Off = 2,

        /// <summary>
        /// The block is damaged below its critical integrity, so the game will not let it run
        /// even when it is switched on. The card shows the red switch-off.
        /// </summary>
        Unavailable = 3
    }
}
